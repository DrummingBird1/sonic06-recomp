/*
 * Game speed on PCs that cannot hold 60 frames a second (issue #15).
 *
 * The game advances a fixed 1/60 s each frame: DocMarathonState::Update
 * (0x22FB0) is given the time the main loop measured, but uses the constant
 * 1/60 instead unless its flag at +0xEC is set. With the flag set it uses the
 * measured time, up to 1/6 s, and the constant above that (a loading screen).
 * The flag is 0 in this build, so at 30 frames a second the whole game runs at
 * half speed; with it set, a stage's TIME counter followed real time at 30
 * frames a second (2/60 s a frame in a Wave Ocean run).
 *
 * The frame rate itself is the game's own: AppMarathon waits for +0x13C
 * vblanks between frames (0 = no wait; the game sets 2 for its cutscenes).
 *
 * Above 60: the frame loop also waits for the flip, which the toolkit
 * completes on its vblank beat (GCM_VBLANK_HZ, 60 by default), and AppMarathon
 * keeps at least 15 ms between frames (0x3A98 us in 0xF00C34 and 0xEFFF28),
 * which alone stops it near 66.
 *
 *   SONIC06_VARIABLE_STEP=1  set the flag: the game keeps real time when frames drop
 *   SONIC06_FRAME_CAP=30     at least 2 vblanks a frame: a steady 30 instead of an uneven 30 to 60
 *   SONIC06_FRAME_CAP=120    (or 90 to 240): the 15 ms becomes 90% of a frame at that rate.
 *                            The launcher also sets GCM_VBLANK_HZ to it, and the flag above,
 *                            without which the game would run that much faster. Cutscenes
 *                            wait vblanks, so the vblank rate must be a multiple of 60
 *                            for them to keep their 30 (see sonic06_app_frame).
 *
 * tools/patch_lift.py puts the calls at the start of the two lifted
 * functions, and sonic06_min_frame_us() in place of the 15 ms. All read their
 * setting once; unset, they change nothing.
 */
#include "ppu_memory.h"   /* ppu_context and the big-endian vm_read/vm_write helpers */
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* func_00022FB0, DocMarathonState::Update(float dt): r3 = this. */
void sonic06_doc_update(ppu_context* ctx)
{
    static int on = -1;
    if (on < 0) {
        const char* e = getenv("SONIC06_VARIABLE_STEP");
        on = e && *e == '1';
        if (on) printf("[sonic06] SONIC06_VARIABLE_STEP=1: the game step follows real time\n");
    }
    if (on) vm_write8((uint32_t)ctx->gpr[3] + 0xEC, 1);
}

/* SONIC06_FRAME_CAP as a number of frames a second; 0 when unset. */
static int frame_cap(void)
{
    static int cap = -1;
    if (cap < 0) { const char* e = getenv("SONIC06_FRAME_CAP"); cap = e ? atoi(e) : 0; if (cap < 0) cap = 0; }
    return cap;
}

/* The least time AppMarathon keeps between two frames, in microseconds: the
 * game's 15000, or 90% of a frame at a cap above 60. Called by the lifted
 * limiters (patch_lift.py), every frame. */
int sonic06_min_frame_us(void)
{
    static int us = -1;
    if (us < 0) {
        const int cap = frame_cap();
        us = cap > 60 && cap <= 240 ? 900000 / cap : 15000;
        if (us != 15000) printf("[sonic06] SONIC06_FRAME_CAP=%d: at least %d us between frames\n", cap, us);
    }
    return us;
}

/* GCM_VBLANK_HZ as the toolkit reads it (30..240, 60 when unset). */
static int vblank_hz(void)
{
    static int hz = -1;
    if (hz < 0) { const char* e = getenv("GCM_VBLANK_HZ"); hz = e ? atoi(e) : 60; if (hz < 30 || hz > 240) hz = 60; }
    return hz;
}

/* func_00E3433C, AppMarathon's per-frame update: r3 = the AppMarathon.
 * At a cap of 30: at least 2 vblanks a frame. With the vblank above 60 Hz:
 * the vblanks the game asks for (2 in a cutscene, 30 frames a second) scaled
 * to the faster beat, or cutscenes, which step per frame, run that much faster
 * (a Wave Ocean scene ran at 69 frames a second with a 120 Hz vblank). */
void sonic06_app_frame(ppu_context* ctx)
{
    static int mode = -1, scale = 1;
    static uint32_t written = 0;
    if (mode < 0) {
        const int hz = vblank_hz();
        mode = frame_cap() == 30 ? 1 : hz > 60 && hz % 60 == 0 ? 2 : 0;
        if (mode == 2) scale = hz / 60;
        if (mode == 1) printf("[sonic06] SONIC06_FRAME_CAP=30: at least 2 vblanks a frame\n");
        if (mode == 2) printf("[sonic06] GCM_VBLANK_HZ=%d: the game's vblank waits x%d\n", hz, scale);
    }
    if (!mode) return;
    const uint32_t field = (uint32_t)ctx->gpr[3] + 0x13C;
    const uint32_t v = vm_read32(field);
    if (mode == 1) {
        if (v < 2) vm_write32(field, 2);
    } else if (v && v != written) {
        /* A value the game set (message 0x11), not the one written here. */
        written = v * (uint32_t)scale;
        vm_write32(field, written);
    }
}
