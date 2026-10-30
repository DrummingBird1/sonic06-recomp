/*
 * cellPamf: the PAMF movie-header reader, for every cellPamf call this title
 * imports. It replaces ps3recomp's module of the same name, which parses a
 * header layout that is not PAMF's, writes through untranslated guest
 * pointers and returns error codes from the wrong range.
 *
 * The format and the SDK behaviour (stream type/channel mapping, how the
 * reader selects streams, what each info struct holds) follow RPCS3's
 * reverse-engineered cellPamf. PAMF headers are big-endian and unaligned, so
 * everything goes through the byte-wise vm_read helpers.
 *
 * Reader state lives in the title's own CellPamfReader (128 bytes, opaque to
 * the title), as guest addresses, so there is no host-side table to keep in
 * step with the guest's allocations.
 */
#include "ppu_memory.h"
#include <stdint.h>

typedef void (*hle_ctx_fn)(ppu_context*);
void ps3_hle_register_ctx(uint32_t nid, const char* name, hle_ctx_fn fn);

#define CELL_OK                             0
#define CELL_PAMF_ERROR_STREAM_NOT_FOUND    0x80610501u
#define CELL_PAMF_ERROR_INVALID_PAMF        0x80610502u
#define CELL_PAMF_ERROR_INVALID_ARG         0x80610503u
#define CELL_PAMF_ERROR_UNKNOWN_TYPE        0x80610504u
#define CELL_PAMF_ERROR_UNSUPPORTED_VERSION 0x80610505u

#define CELL_PAMF_ATTRIBUTE_VERIFY_ON 1

/* CellPamfStreamType */
enum {
    TYPE_AVC = 0, TYPE_M2V = 1, TYPE_ATRAC3PLUS = 2, TYPE_PAMF_LPCM = 3,
    TYPE_AC3 = 4, TYPE_USER_DATA = 5,
    TYPE_VIDEO = 20, TYPE_AUDIO = 21, TYPE_UNK = 22,
};

/* stream_coding_type values in the file */
enum {
    CODING_M2V = 0x02, CODING_AVC = 0x1B, CODING_LPCM = 0x80,
    CODING_AC3 = 0x81, CODING_ATRAC3PLUS = 0xDC, CODING_USER_DATA = 0xDD,
};

/* PamfHeader offsets. The sequence info follows 0x50 bytes of file header;
 * this title's movies all have one grouping period with one group. */
#define HDR_MAGIC          0x00
#define HDR_VERSION        0x04
#define HDR_HEADER_SIZE    0x08   /* in 0x800-byte units */
#define HDR_DATA_SIZE      0x0C   /* in 0x800-byte units */
#define SEQ_GP_NUM         0x6F   /* grouping_period_num */
#define GP_GROUP_NUM       0x81   /* first grouping period's group_num */
#define GROUP_OFF          0x82   /* first group */
#define GROUP_STREAM_NUM   5      /* stream_num, relative to the group */
#define GROUP_STREAMS      6      /* first PamfStreamHeader, relative to the group */
#define STREAM_SIZE        48

/* PamfStreamHeader fields */
#define SH_CODING_TYPE     0
#define SH_STREAM_ID       4
#define SH_PRIVATE_ID      5
#define SH_DATA            16     /* codec-specific block */

/* Our CellPamfReader layout (the title only allocates 128 bytes). */
#define RD_HEADER_SIZE     0x00   /* u64 */
#define RD_DATA_SIZE       0x08   /* u64 */
#define RD_ATTRIBUTE       0x10
#define RD_HEADER          0x14   /* guest address of the PamfHeader */
#define RD_GROUP           0x18   /* current group, 0 if none */
#define RD_STREAM_INDEX    0x1C
#define RD_STREAM          0x20   /* current PamfStreamHeader */
#define READER_SIZE        128

static void ret(ppu_context* ctx, uint32_t rc)
{
    ctx->gpr[3] = (uint64_t)(int64_t)(int32_t)rc;
}

static uint32_t arg32(ppu_context* ctx, int n) { return (uint32_t)ctx->gpr[3 + n]; }
static uint8_t arg8(ppu_context* ctx, int n) { return (uint8_t)ctx->gpr[3 + n]; }

/* Type and channel -> the PES ids the stream is multiplexed under. */
static int type_channel_to_stream(uint8_t type, uint8_t ch, uint8_t* coding,
                                  uint8_t* stream_id, uint8_t* private_id)
{
    ch &= 0x0F;
    switch (type) {
    case TYPE_AVC:        *coding = CODING_AVC;        *stream_id = 0xE0 | ch; *private_id = 0;         return 0;
    case TYPE_M2V:        *coding = CODING_M2V;        *stream_id = 0xE0 | ch; *private_id = 0;         return 0;
    case TYPE_ATRAC3PLUS: *coding = CODING_ATRAC3PLUS; *stream_id = 0xBD;      *private_id = ch;        return 0;
    case TYPE_PAMF_LPCM:  *coding = CODING_LPCM;       *stream_id = 0xBD;      *private_id = 0x40 | ch; return 0;
    case TYPE_AC3:        *coding = CODING_AC3;        *stream_id = 0xBD;      *private_id = 0x30 | ch; return 0;
    case TYPE_USER_DATA:  *coding = CODING_USER_DATA;  *stream_id = 0xBD;      *private_id = 0x20 | ch; return 0;
    default: return -1;   /* the PSMF types never occur in a PAMF file */
    }
}

/* A stream header -> its CellPamfStreamType and channel. */
static int stream_to_type_channel(uint32_t sh, uint8_t* type, uint8_t* ch)
{
    uint8_t stream_id = vm_read8(sh + SH_STREAM_ID);
    uint8_t private_id = vm_read8(sh + SH_PRIVATE_ID);
    switch (vm_read8(sh + SH_CODING_TYPE)) {
    case CODING_AVC:        *type = TYPE_AVC;        *ch = stream_id & 0x0F;  return 0;
    case CODING_M2V:        *type = TYPE_M2V;        *ch = stream_id & 0x0F;  return 0;
    case CODING_ATRAC3PLUS: *type = TYPE_ATRAC3PLUS; *ch = private_id & 0x0F; return 0;
    case CODING_LPCM:       *type = TYPE_PAMF_LPCM;  *ch = private_id & 0x0F; return 0;
    case CODING_AC3:        *type = TYPE_AC3;        *ch = private_id & 0x0F; return 0;
    case CODING_USER_DATA:  *type = TYPE_USER_DATA;  *ch = private_id & 0x0F; return 0;
    default: return -1;
    }
}

/* The VIDEO/AUDIO/UNK wildcards match a family of concrete types. */
static int same_stream_type(uint8_t type, uint8_t requested)
{
    switch (requested) {
    case TYPE_VIDEO: return type == TYPE_AVC || type == TYPE_M2V;
    case TYPE_AUDIO: return type == TYPE_ATRAC3PLUS || type == TYPE_AC3 || type == TYPE_PAMF_LPCM;
    case TYPE_UNK:   return type == TYPE_PAMF_LPCM;
    default:         return type == requested;
    }
}

static uint32_t group_stream(uint32_t group, uint32_t i)
{
    return group + GROUP_STREAMS + i * STREAM_SIZE;
}

static uint8_t group_stream_num(uint32_t self)
{
    uint32_t group = vm_read32(self + RD_GROUP);
    return group ? vm_read8(group + GROUP_STREAM_NUM) : 0;
}

static void select_stream(uint32_t self, uint32_t index)
{
    vm_write32(self + RD_STREAM_INDEX, index);
    vm_write32(self + RD_STREAM, group_stream(vm_read32(self + RD_GROUP), index));
}

/* Header and data sizes, checked against the file size when one is given. */
static uint32_t header_and_data_size(uint32_t hdr, uint64_t file_size, uint64_t* header, uint64_t* data)
{
    if (vm_read32(hdr + HDR_MAGIC) != 0x50414D46u)   /* "PAMF" */
        return CELL_PAMF_ERROR_UNKNOWN_TYPE;
    *header = (uint64_t)vm_read32(hdr + HDR_HEADER_SIZE) * 0x800u;
    *data = (uint64_t)vm_read32(hdr + HDR_DATA_SIZE) * 0x800u;
    if (*header == 0 || (file_size != 0 && *header + *data != file_size))
        return CELL_PAMF_ERROR_INVALID_PAMF;
    return CELL_OK;
}

/* cellPamfGetHeaderSize(const PamfHeader*, u64 fileSize, u64* headerSize) */
static void hle_cellPamfGetHeaderSize(ppu_context* ctx)
{
    uint64_t header, data;
    uint32_t rc = header_and_data_size(arg32(ctx, 0), ctx->gpr[4], &header, &data);
    if (rc == CELL_OK && arg32(ctx, 2))
        vm_write64(arg32(ctx, 2), header);
    ret(ctx, rc);
}

/* cellPamfGetStreamOffsetAndSize(const PamfHeader*, u64 fileSize, u64* offset,
 * u64* size): the elementary streams start right after the header. */
static void hle_cellPamfGetStreamOffsetAndSize(ppu_context* ctx)
{
    uint64_t header, data;
    uint32_t rc = header_and_data_size(arg32(ctx, 0), ctx->gpr[4], &header, &data);
    if (rc == CELL_OK) {
        if (arg32(ctx, 2)) vm_write64(arg32(ctx, 2), header);
        if (arg32(ctx, 3)) vm_write64(arg32(ctx, 3), data);
    }
    ret(ctx, rc);
}

/* cellPamfReaderInitialize(CellPamfReader*, const PamfHeader*, u64 fileSize,
 * u32 attribute). Verification is opt-in, as on the SDK. */
static void hle_cellPamfReaderInitialize(ppu_context* ctx)
{
    uint32_t self = arg32(ctx, 0), hdr = arg32(ctx, 1), attribute = arg32(ctx, 3);
    uint64_t file_size = ctx->gpr[5];
    if (!self || !hdr) { ret(ctx, CELL_PAMF_ERROR_INVALID_ARG); return; }

    for (uint32_t i = 0; i < READER_SIZE; i += 8)
        vm_write64(self + i, 0);
    vm_write32(self + RD_ATTRIBUTE, attribute);
    vm_write32(self + RD_STREAM_INDEX, 0xFFFFFFFFu);

    if (attribute & CELL_PAMF_ATTRIBUTE_VERIFY_ON) {
        uint32_t version = vm_read32(hdr + HDR_VERSION);
        uint64_t header, data;
        uint32_t rc = header_and_data_size(hdr, file_size, &header, &data);
        if (rc != CELL_OK) { ret(ctx, rc); return; }
        if (version != 0x30303430u && version != 0x30303431u) {   /* "0040", "0041" */
            ret(ctx, CELL_PAMF_ERROR_UNSUPPORTED_VERSION);
            return;
        }
    }
    vm_write64(self + RD_HEADER_SIZE, (uint64_t)vm_read32(hdr + HDR_HEADER_SIZE) * 0x800u);
    vm_write64(self + RD_DATA_SIZE, (uint64_t)vm_read32(hdr + HDR_DATA_SIZE) * 0x800u);
    vm_write32(self + RD_HEADER, hdr);

    if (vm_read8(hdr + SEQ_GP_NUM) == 0) { ret(ctx, CELL_PAMF_ERROR_INVALID_PAMF); return; }
    if (vm_read8(hdr + GP_GROUP_NUM) != 0) {
        vm_write32(self + RD_GROUP, hdr + GROUP_OFF);
        if (group_stream_num(self) != 0)
            select_stream(self, 0);
    }
    ret(ctx, CELL_OK);
}

/* u8 cellPamfReaderGetNumberOfSpecificStreams(CellPamfReader*, u8 streamType) */
static void hle_cellPamfReaderGetNumberOfSpecificStreams(ppu_context* ctx)
{
    uint32_t self = arg32(ctx, 0);
    uint8_t requested = arg8(ctx, 1), type, ch, found = 0;
    uint8_t n = group_stream_num(self);
    for (uint8_t i = 0; i < n; i++)
        if (stream_to_type_channel(group_stream(vm_read32(self + RD_GROUP), i), &type, &ch) == 0)
            found += (uint8_t)same_stream_type(type, requested);
    ctx->gpr[3] = found;
}

/* cellPamfReaderSetStreamWithTypeAndIndex(CellPamfReader*, u8 streamType,
 * u8 streamIndex): selects the streamIndex-th stream of that type and returns
 * its overall index. */
static void hle_cellPamfReaderSetStreamWithTypeAndIndex(ppu_context* ctx)
{
    uint32_t self = arg32(ctx, 0);
    uint8_t requested = arg8(ctx, 1), index = arg8(ctx, 2), type, ch;
    uint8_t n = group_stream_num(self);
    uint32_t found = 0;
    if (index >= n) { ret(ctx, CELL_PAMF_ERROR_INVALID_ARG); return; }

    for (uint8_t i = 0; i < n; i++) {
        if (stream_to_type_channel(group_stream(vm_read32(self + RD_GROUP), i), &type, &ch) != 0)
            continue;
        found += (uint32_t)same_stream_type(type, requested);
        if (found > index) {
            select_stream(self, i);
            ctx->gpr[3] = i;
            return;
        }
    }
    ret(ctx, CELL_PAMF_ERROR_STREAM_NOT_FOUND);
}

/* cellPamfReaderGetStreamTypeAndChannel(CellPamfReader*, u8* type, u8* ch) */
static void hle_cellPamfReaderGetStreamTypeAndChannel(ppu_context* ctx)
{
    uint32_t sh = vm_read32(arg32(ctx, 0) + RD_STREAM);
    uint8_t type, ch;
    if (!sh || stream_to_type_channel(sh, &type, &ch) != 0) {
        ret(ctx, CELL_PAMF_ERROR_STREAM_NOT_FOUND);
        return;
    }
    if (arg32(ctx, 1)) vm_write8(arg32(ctx, 1), type);
    if (arg32(ctx, 2)) vm_write8(arg32(ctx, 2), ch);
    ret(ctx, CELL_OK);
}

/* cellPamfStreamTypeToEsFilterId(u8 type, u8 ch, CellCodecEsFilterId*): the
 * demuxer filter for a stream is its PES stream_id and private stream id.
 * CellCodecEsFilterId is four u32s: major, minor, supplementalInfo1 (set
 * for AVC) and supplementalInfo2. */
static void hle_cellPamfStreamTypeToEsFilterId(ppu_context* ctx)
{
    uint8_t type = arg8(ctx, 0), ch = arg8(ctx, 1), coding, stream_id, private_id;
    uint32_t out = arg32(ctx, 2);
    if (!out || type_channel_to_stream(type, ch, &coding, &stream_id, &private_id) != 0) {
        ret(ctx, CELL_PAMF_ERROR_INVALID_ARG);
        return;
    }
    vm_write32(out + 0, stream_id);
    vm_write32(out + 4, private_id);
    vm_write32(out + 8, type == TYPE_AVC);
    vm_write32(out + 12, 0);
    ret(ctx, CELL_OK);
}

/* cellPamfReaderGetStreamInfo(CellPamfReader*, void* info, u32 size): the
 * CellPamf*Info struct for the current stream's type. */
static void hle_cellPamfReaderGetStreamInfo(ppu_context* ctx)
{
    uint32_t sh = vm_read32(arg32(ctx, 0) + RD_STREAM);
    uint32_t info = arg32(ctx, 1), size = arg32(ctx, 2);
    uint32_t d = sh + SH_DATA;
    uint8_t type, ch;
    if (!sh || stream_to_type_channel(sh, &type, &ch) != 0) {
        ret(ctx, CELL_PAMF_ERROR_INVALID_PAMF);
        return;
    }

    switch (type) {
    case TYPE_AVC: {   /* CellPamfAvcInfo, 0x20 bytes */
        if (size < 0x20) { ret(ctx, CELL_PAMF_ERROR_INVALID_ARG); return; }
        uint8_t x2 = vm_read8(d + 2), aspect = vm_read8(d + 3), x14 = vm_read8(d + 20), x18 = vm_read8(d + 24);
        int signal = (x2 & 0x40) != 0;
        vm_write8(info + 0, vm_read8(d + 0));                 /* profileIdc */
        vm_write8(info + 1, vm_read8(d + 1));                 /* levelIdc */
        vm_write8(info + 2, (x2 & 0x80) >> 7);                /* frameMbsOnlyFlag */
        vm_write8(info + 3, signal);                          /* videoSignalInfoFlag */
        vm_write8(info + 4, (uint8_t)((x2 & 0x0F) - 1));      /* frameRateInfo */
        vm_write8(info + 5, aspect);                          /* aspectRatioIdc */
        vm_write16(info + 6, aspect == 0xFF ? vm_read16(d + 4) : 0);   /* sarWidth */
        vm_write16(info + 8, aspect == 0xFF ? vm_read16(d + 6) : 0);   /* sarHeight */
        vm_write16(info + 10, (uint16_t)(vm_read8(d + 9) * 16));       /* horizontalSize */
        vm_write16(info + 12, (uint16_t)(vm_read8(d + 11) * 16));      /* verticalSize */
        vm_write16(info + 14, vm_read16(d + 12));             /* frameCrop left */
        vm_write16(info + 16, vm_read16(d + 14));             /*           right */
        vm_write16(info + 18, vm_read16(d + 16));             /*           top */
        vm_write16(info + 20, vm_read16(d + 18));             /*           bottom */
        vm_write8(info + 22, signal ? x14 >> 5 : 0);          /* videoFormat */
        vm_write8(info + 23, signal ? (x14 & 0x10) >> 4 : 0); /* videoFullRangeFlag */
        vm_write8(info + 24, signal ? vm_read8(d + 21) : 0);  /* colourPrimaries */
        vm_write8(info + 25, signal ? vm_read8(d + 22) : 0);  /* transferCharacteristics */
        vm_write8(info + 26, signal ? vm_read8(d + 23) : 0);  /* matrixCoefficients */
        vm_write8(info + 27, (x18 & 0x80) >> 7);              /* entropyCodingModeFlag */
        vm_write8(info + 28, (x18 & 0x40) >> 6);              /* deblockingFilterFlag */
        vm_write8(info + 29, (x18 & 0x30) >> 4);              /* minNumSlicePerPictureIdc */
        vm_write8(info + 30, x18 & 0x03);                     /* nfwIdc */
        vm_write8(info + 31, vm_read8(d + 25));               /* maxMeanBitrate */
        break;
    }
    case TYPE_ATRAC3PLUS:
    case TYPE_AC3:     /* CellPamfAtrac3plusInfo / CellPamfAc3Info: u32 fs code, u8 channels */
        if (size < 8) { ret(ctx, CELL_PAMF_ERROR_INVALID_ARG); return; }
        vm_write32(info + 0, vm_read8(d + 3) & 0x0F);
        vm_write8(info + 4, vm_read8(d + 2) & 0x0F);
        break;
    case TYPE_PAMF_LPCM:   /* CellPamfLpcmInfo: u32 fs code, u8 channels, u16 bit length */
        if (size < 8) { ret(ctx, CELL_PAMF_ERROR_INVALID_ARG); return; }
        vm_write32(info + 0, vm_read8(d + 3) & 0x0F);
        vm_write8(info + 4, vm_read8(d + 2) & 0x0F);
        vm_write16(info + 6, vm_read8(d + 4) >> 6);
        break;
    default:   /* M2V and user data: not in this title's movies */
        ret(ctx, CELL_PAMF_ERROR_INVALID_ARG);
        return;
    }
    ret(ctx, CELL_OK);
}

__attribute__((constructor))
static void cell_pamf_register(void)
{
    ps3_hle_register_ctx(0xCA8181C1u, "cellPamfGetHeaderSize", hle_cellPamfGetHeaderSize);
    ps3_hle_register_ctx(0x44F5C9E3u, "cellPamfGetStreamOffsetAndSize", hle_cellPamfGetStreamOffsetAndSize);
    ps3_hle_register_ctx(0xB8436EE5u, "cellPamfReaderInitialize", hle_cellPamfReaderInitialize);
    ps3_hle_register_ctx(0xD0230671u, "cellPamfReaderGetNumberOfSpecificStreams",
                         hle_cellPamfReaderGetNumberOfSpecificStreams);
    ps3_hle_register_ctx(0x28B4E2C1u, "cellPamfReaderSetStreamWithTypeAndIndex",
                         hle_cellPamfReaderSetStreamWithTypeAndIndex);
    ps3_hle_register_ctx(0x9AB20793u, "cellPamfReaderGetStreamTypeAndChannel",
                         hle_cellPamfReaderGetStreamTypeAndChannel);
    ps3_hle_register_ctx(0x01067E22u, "cellPamfStreamTypeToEsFilterId", hle_cellPamfStreamTypeToEsFilterId);
    ps3_hle_register_ctx(0x67FD273Bu, "cellPamfReaderGetStreamInfo", hle_cellPamfReaderGetStreamInfo);
}
