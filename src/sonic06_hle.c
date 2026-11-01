/*
 * HLE handlers this title imports that the ps3recomp runtime does not
 * register. Each is checked against the SDK headers and RPCS3's module of the
 * same name. They register from a constructor: the context table in
 * ppu_hle.cpp is plain static storage, so it is ready before main runs, and
 * context handlers are dispatched before ps3recomp's generic table, so the
 * cellResc wrappers below take precedence over the functions they wrap.
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

/* Presenting through cellResc. The title renders each frame into a resc
 * source surface (cellRescSetSrc) and asks resc to scale it into a display
 * buffer and flip. ps3recomp's resc never registers display buffers, so its
 * convert-and-flip found none and never flipped, and nothing would copy the
 * source anyway: the D3D12 backend keeps that surface as a GPU render target
 * (stored upside down relative to the screen, so a plain copy of it shows the
 * frame flipped).
 *
 * These wrappers register resc's destination buffers with GCM the way libresc
 * does, so convert-and-flip flips, and also register each source surface as a
 * display buffer. The backend then draws the title's final pass straight into
 * the backbuffer, the right way up, which is what resc's 1:1 scale would have
 * produced (720p source and output). The wrapped ps3recomp calls keep their
 * own state and logging. Pointer arguments to them are guest addresses, as
 * the HLE dispatcher passes them. */
int32_t cellRescSetSrc(int32_t index, const void* src);
int32_t cellRescSetDsts(uint32_t displayMode, const void* dsts);
int32_t cellRescSetBufferAddress(const void* colorBuf, const void* vertexBuf, const void* fragmentBuf);
int32_t cellGcmSetDisplayBuffer(uint32_t id, uint32_t offset, uint32_t pitch, uint32_t width, uint32_t height);
int32_t cellGcmAddressToOffset(uint32_t address, uint32_t* offset);

#define RESC_SRC_MAX 4
#define RESC_DST_BUFFERS 2   /* cellRescGetNumColorBuffers outside the PAL modes */

static uint32_t s_resc_mode, s_resc_pitch, s_resc_height_align;

/* cellRescSetSrc(s32 index, const CellRescSrc*): CellRescSrc is format @0,
 * pitch @4, width @8, height @10, offset @12. Source i becomes display buffer
 * RESC_DST_BUFFERS + i. */
static void hle_cellRescSetSrc(ppu_context* ctx)
{
    int32_t index = (int32_t)ctx->gpr[3];
    uint32_t src = (uint32_t)ctx->gpr[4];
    if (src && index >= 0 && index < RESC_SRC_MAX)
        cellGcmSetDisplayBuffer(RESC_DST_BUFFERS + (uint32_t)index, vm_read32(src + 12),
                                vm_read32(src + 4), vm_read16(src + 8), vm_read16(src + 10));
    ret(ctx, (uint32_t)cellRescSetSrc(index, (const void*)(uintptr_t)src));
}

/* cellRescSetDsts(u32 displayMode, const CellRescDsts*): format, pitch,
 * heightAlign of the destination buffers for that mode. */
static void hle_cellRescSetDsts(ppu_context* ctx)
{
    uint32_t mode = (uint32_t)ctx->gpr[3], dsts = (uint32_t)ctx->gpr[4];
    if (dsts) {
        s_resc_mode = mode;
        s_resc_pitch = vm_read32(dsts + 4);
        s_resc_height_align = vm_read32(dsts + 8);
    }
    ret(ctx, (uint32_t)cellRescSetDsts(mode, (const void*)(uintptr_t)dsts));
}

/* cellRescSetBufferAddress(void* colorBuf, void* vertexBuf, void* fragmentBuf):
 * the destination buffers sit back to back in colorBuf (RSX local memory). */
static void hle_cellRescSetBufferAddress(ppu_context* ctx)
{
    uint32_t color = (uint32_t)ctx->gpr[3];
    uint32_t w = 1280, h = 720;
    switch (s_resc_mode) {
    case 0x1: w = 720;  h = 480;  break;
    case 0x2: w = 720;  h = 576;  break;
    case 0x8: w = 1920; h = 1080; break;
    }
    uint32_t pitch = s_resc_pitch ? s_resc_pitch : w * 4;
    uint32_t align = s_resc_height_align ? s_resc_height_align : 1;
    uint32_t size = pitch * ((h + align - 1) / align * align);
    /* AddressToOffset writes through a guest pointer: use the red zone below
     * the caller's stack pointer, which a leaf callee may clobber. */
    uint32_t scratch = (uint32_t)ctx->gpr[1] - 16;
    if (color && cellGcmAddressToOffset(color, (uint32_t*)(uintptr_t)scratch) == 0) {
        uint32_t offset = vm_read32(scratch);
        for (uint32_t i = 0; i < RESC_DST_BUFFERS; i++)
            cellGcmSetDisplayBuffer(i, offset + i * size, pitch, w, h);
    }
    ret(ctx, (uint32_t)cellRescSetBufferAddress((const void*)(uintptr_t)color,
                                                (const void*)(uintptr_t)(uint32_t)ctx->gpr[4],
                                                (const void*)(uintptr_t)(uint32_t)ctx->gpr[5]));
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

/* cellSurMixerStart reads the key from a global that the mixer's own thread
 * sets (0x134B214) just before it creates its event queue. On hardware that
 * thread has the higher priority and has always run first. Here both run at
 * once, and when the caller won it registered key 0: no audio event ever reached
 * the mixer, no sound played, and the first movie's audio ring never drained, so
 * the player could not stop (the hang at 0xB796BC). cellAudio looks the queue up
 * by key on every block, so the key the mixer is about to use can be named now. */
#define SURMIXER_KEY 0x8000CAFE02460300ull

int32_t cellAudioSetNotifyEventQueue(uint64_t key);

static void hle_cellAudioSetNotifyEventQueue(ppu_context* ctx)
{
    uint64_t key = ctx->gpr[3];
    ret(ctx, (uint32_t)cellAudioSetNotifyEventQueue(key ? key : SURMIXER_KEY));
}

__attribute__((constructor))
static void sonic06_hle_register(void)
{
    ps3_hle_register_ctx(0x377E0CD9u, "cellAudioSetNotifyEventQueue", hle_cellAudioSetNotifyEventQueue);
    ps3_hle_register_ctx(0x3A33C1FDu, "_cellGcmFunc15", hle_cellGcmFunc15);
    ps3_hle_register_ctx(0x01220224u, "cellRescGcmSurface2RescSrc", hle_cellRescGcmSurface2RescSrc);
    ps3_hle_register_ctx(0x0D3C22CEu, "cellRescSetWaitFlip", hle_cellRescSetWaitFlip);
    ps3_hle_register_ctx(0x6CD0F95Fu, "cellRescSetSrc", hle_cellRescSetSrc);
    ps3_hle_register_ctx(0x10DB5B1Au, "cellRescSetDsts", hle_cellRescSetDsts);
    ps3_hle_register_ctx(0x8107277Cu, "cellRescSetBufferAddress", hle_cellRescSetBufferAddress);
    ps3_hle_register_ctx(0xD1CA0503u, "cellRescVideoOutResolutionId2RescBufferMode",
                         hle_cellRescVideoOutResolutionId2RescBufferMode);
}
