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
 *   SONIC06_VARIABLE_STEP=1  set the flag: the game keeps real time when frames drop
 *   SONIC06_FRAME_CAP=30     at least 2 vblanks a frame: a steady 30 instead of an uneven 30 to 60
 *
 * tools/patch_lift.py puts the calls at the start of the two lifted
 * functions. Both read their setting once; unset, they change nothing.
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

/* func_00E3433C, AppMarathon's per-frame update: r3 = the AppMarathon. */
void sonic06_app_frame(ppu_context* ctx)
{
    static int vblanks = -1;
    if (vblanks < 0) {
        const char* e = getenv("SONIC06_FRAME_CAP");
        vblanks = e && !strcmp(e, "30") ? 2 : 0;
        if (vblanks) printf("[sonic06] SONIC06_FRAME_CAP=30: at least 2 vblanks a frame\n");
    }
    if (!vblanks) return;
    const uint32_t field = (uint32_t)ctx->gpr[3] + 0x13C;
    if (vm_read32(field) < (uint32_t)vblanks) vm_write32(field, (uint32_t)vblanks);
}
