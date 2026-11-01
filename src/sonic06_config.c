/*
 * ps3recomp settings this title needs. The toolkit reads them from the
 * environment when it first uses them, so setting them from a constructor
 * (before main) makes them the port's defaults. A value already in the
 * environment wins, so each can still be overridden for an experiment.
 *
 * On Windows the harness routes getenv through a cache that also remembers
 * names it did not find (cached_getenv in boot_main.cpp), so the check and
 * the store go to the process environment directly: a getenv here would cache
 * the name as unset for good.
 */
#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#else
#include <stdlib.h>
#endif

static void set_default(const char* name, const char* value)
{
#ifdef _WIN32
    if (!GetEnvironmentVariableA(name, NULL, 0))
        SetEnvironmentVariableA(name, value);
#else
    setenv(name, value, 0);
#endif
}

__attribute__((constructor))
static void sonic06_config(void)
{
    /* The D3D12 backend samples the current frame in place of any texture the
     * size of the display (a screen-copy trick another title's reflections
     * needed). Every movie frame here is 1280x720, so the SEGA logo and the
     * title-screen background showed the black frame instead of the movie. */
    set_default("SCREEN_AS_TEX", "0");

    /* SPU reads of the inbound mailbox and the signal-notification channels
     * return at once with 0 unless this is set; on hardware they wait for the
     * PPU. The FrustumCullingTask jobs wait for their commands on SNR1, so
     * without it they ran "command 0" at start-up, polled EA 0 until the
     * runtime halted them, and the game then waited for their reply
     * forever. */
    set_default("SPU_CH_BLOCK", "1");

    /* libmixer's cond handshake with its strip threads locks for 100 us and
     * ignores the result (0x134DEB0). On hardware its priority makes that safe.
     * Here the holder can lose its core for longer, the lock times out, the
     * signal goes out while the strip thread is on its way to the wait, and
     * both then wait forever: every sound stops for the rest of the run. No
     * lock in this title with a timeout is held that long on purpose. */
    set_default("PS3_LOCK_TIMEOUT_MIN_US", "1000000");

    /* The frame loop waits on a semaphore that the flip handler posts, and
     * runs one fixed 1/60 s step per frame. cellGcm also calls the flip handler
     * when a flip is requested, so the semaphore was always open and the game
     * ran at the host's frame rate: thousands of steps a second, and the title
     * screen's 30 s idle timer ran out before START could be pressed. With this
     * the handler only runs when the flip completes on the 60 Hz beat. */
    set_default("GCM_FLIPCB_ONTICK", "1");
}
