/*
 * HLE handlers this title imports that the ps3recomp runtime does not
 * register. Each is checked against the SDK headers and RPCS3's module of the
 * same name. They register from a constructor: the context table in
 * ppu_hle.cpp is plain static storage, so it is ready before main runs, and
 * none of these NIDs has another handler to compete with.
 */
#include "ppu_memory.h"   /* ppu_context and the big-endian vm_read/vm_write helpers */
#include <stdint.h>
#include <stdio.h>

typedef void (*hle_ctx_fn)(ppu_context*);
void ps3_hle_register_ctx(uint32_t nid, const char* name, hle_ctx_fn fn);

#define CELL_OK                        0
#define CELL_RESC_ERROR_BAD_ARGUMENT   0x80210303u

static void ret(ppu_context* ctx, uint32_t rc)
{
    ctx->gpr[3] = (uint64_t)(int64_t)(int32_t)rc;
}

/* _cellGcmFunc15(CellGcmContextData*): an SDK-internal call made during GCM
 * setup. RPCS3 does nothing for it either. */
static void hle_cellGcmFunc15(ppu_context* ctx)
{
    ret(ctx, CELL_OK);
}

/* cellRescGcmSurface2RescSrc(const CellGcmSurface*, CellRescSrc*): the SDK
 * fills the resc source from colour target 0 of a GCM surface. The surface
 * format values are the same numbers resc uses (A8R8G8B8 = 8,
 * F_W16Z16Y16X16 = 0xB). CellGcmSurface: colorFormat @2, colorOffset[0] @8,
 * colorPitch[0] @24, width @52, height @54. CellRescSrc: format @0, pitch @4,
 * width @8, height @10, offset @12. */
static void hle_cellRescGcmSurface2RescSrc(ppu_context* ctx)
{
    uint32_t surface = (uint32_t)ctx->gpr[3];
    uint32_t src = (uint32_t)ctx->gpr[4];
    if (!surface || !src) { ret(ctx, CELL_RESC_ERROR_BAD_ARGUMENT); return; }

    vm_write32(src + 0,  vm_read8(surface + 2));
    vm_write32(src + 4,  vm_read32(surface + 24));
    vm_write16(src + 8,  vm_read16(surface + 52));
    vm_write16(src + 10, vm_read16(surface + 54));
    vm_write32(src + 12, vm_read32(surface + 8));
    ret(ctx, CELL_OK);
}

/* cellRescSetWaitFlip(CellGcmContextData*): would put a wait-for-flip into
 * the command buffer. The runtime's flip is synchronous, so there is nothing
 * to wait on; RPCS3 also returns OK. */
static void hle_cellRescSetWaitFlip(ppu_context* ctx)
{
    ret(ctx, CELL_OK);
}

/* cellRescVideoOutResolutionId2RescBufferMode(u32 resolutionId, u32* mode):
 * maps a CELL_VIDEO_OUT_RESOLUTION_* id to the CELL_RESC_* buffer mode the
 * title then passes to cellRescSetDisplayMode. */
static void hle_cellRescVideoOutResolutionId2RescBufferMode(ppu_context* ctx)
{
    uint32_t id = (uint32_t)ctx->gpr[3];
    uint32_t out = (uint32_t)ctx->gpr[4];
    uint32_t mode;
    switch (id) {
    case 1: mode = 0x08; break;   /* 1080 -> CELL_RESC_1920x1080 */
    case 2: mode = 0x04; break;   /* 720  -> CELL_RESC_1280x720  */
    case 4: mode = 0x01; break;   /* 480  -> CELL_RESC_720x480   */
    case 5: mode = 0x02; break;   /* 576  -> CELL_RESC_720x576   */
    default: ret(ctx, CELL_RESC_ERROR_BAD_ARGUMENT); return;
    }
    if (!out) { ret(ctx, CELL_RESC_ERROR_BAD_ARGUMENT); return; }
    vm_write32(out, mode);
    ret(ctx, CELL_OK);
}

/* PAMF movie headers (HD_SEGA.pam, title_loop_GBn.pam, the event movies).
 * PamfHeader: magic "PAMF" @0, version @4, header_size @8 and data_size @12,
 * both big-endian counts of 0x800-byte sectors. Same checks as RPCS3's
 * pamfGetHeaderAndDataSize; the SDK error codes are RPCS3's (cellPamf.h). */
#define CELL_PAMF_ERROR_INVALID_PAMF  0x80610502u
#define CELL_PAMF_ERROR_UNKNOWN_TYPE  0x80610504u

static uint32_t pamf_sizes(uint32_t hdr, uint64_t file_size, uint64_t* header, uint64_t* data)
{
    if (vm_read32(hdr) != 0x50414D46u)   /* "PAMF" */
        return CELL_PAMF_ERROR_UNKNOWN_TYPE;
    *header = (uint64_t)vm_read32(hdr + 8) * 0x800u;
    *data = (uint64_t)vm_read32(hdr + 12) * 0x800u;
    if (*header == 0 || (file_size != 0 && *header + *data != file_size))
        return CELL_PAMF_ERROR_INVALID_PAMF;
    return CELL_OK;
}

/* cellPamfGetHeaderSize(const PamfHeader*, u64 fileSize, u64* headerSize) */
static void hle_cellPamfGetHeaderSize(ppu_context* ctx)
{
    uint64_t header, data;
    uint32_t rc = pamf_sizes((uint32_t)ctx->gpr[3], ctx->gpr[4], &header, &data);
    if (rc == CELL_OK && (uint32_t)ctx->gpr[5])
        vm_write64((uint32_t)ctx->gpr[5], header);
    ret(ctx, rc);
}

/* cellPamfGetStreamOffsetAndSize(const PamfHeader*, u64 fileSize, u64* offset,
 * u64* size): the elementary streams start right after the header. */
static void hle_cellPamfGetStreamOffsetAndSize(ppu_context* ctx)
{
    uint64_t header, data;
    uint32_t rc = pamf_sizes((uint32_t)ctx->gpr[3], ctx->gpr[4], &header, &data);
    if (rc == CELL_OK) {
        if ((uint32_t)ctx->gpr[5]) vm_write64((uint32_t)ctx->gpr[5], header);
        if ((uint32_t)ctx->gpr[6]) vm_write64((uint32_t)ctx->gpr[6], data);
    }
    ret(ctx, rc);
}

__attribute__((constructor))
static void sonic06_hle_register(void)
{
    ps3_hle_register_ctx(0xCA8181C1u, "cellPamfGetHeaderSize", hle_cellPamfGetHeaderSize);
    ps3_hle_register_ctx(0x44F5C9E3u, "cellPamfGetStreamOffsetAndSize", hle_cellPamfGetStreamOffsetAndSize);
    ps3_hle_register_ctx(0x3A33C1FDu, "_cellGcmFunc15", hle_cellGcmFunc15);
    ps3_hle_register_ctx(0x01220224u, "cellRescGcmSurface2RescSrc", hle_cellRescGcmSurface2RescSrc);
    ps3_hle_register_ctx(0x0D3C22CEu, "cellRescSetWaitFlip", hle_cellRescSetWaitFlip);
    ps3_hle_register_ctx(0xD1CA0503u, "cellRescVideoOutResolutionId2RescBufferMode",
                         hle_cellRescVideoOutResolutionId2RescBufferMode);
}
