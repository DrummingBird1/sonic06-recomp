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

    /* The FIFO walker sleeps up to this long when it has drained everything,
     * unless a dry fence poll kicks it. The main thread here waits in a 1 ms
     * usleep poll instead, so new commands sat for up to 4 ms before the RSX
     * saw them, on every hand-off between the two, and the first scene ran
     * 25.8 flips/s against 28.4 at 1 ms (5 alternating pairs). */
    set_default("GCM_TICK_MS", "1");

    /* The keyboard as a PC player holds it, in place of the toolkit's
     * controller-shaped map (arrows, Z/X/A/S): WASD or the arrows move, Space
     * jumps. Enter is CROSS too, because the menus confirm with CROSS, which
     * also passes the title screen, and START does nothing on the main menu
     * (measured). Esc is START, the pause menu. CIRCLE, back in the menus, is
     * the left Ctrl and the right mouse button: not Shift, whose fifth press in
     * a row brings up Windows' Sticky Keys prompt over the game. SQUARE is E
     * and the left button, TRIANGLE F, L1/R1 Q/R, L2/R2 1/2, R3 the middle
     * button, SELECT Tab. The launcher shows the same map (DefaultKeyMap in
     * launcher/Game.cs) and sends PAD_KEYS only when the player changed it. */
    set_default("PAD_KEYS", "up=0x57+0x26,down=0x53+0x28,left=0x41+0x25,right=0x44+0x27,"
                            "cross=0x20+0x0D,circle=0xA2+0x02,square=0x45+0x01,triangle=0x46,"
                            "l1=0x51,r1=0x52,l2=0x31,r2=0x32,r3=0x04,start=0x1B,select=0x09");

    /* Esc is START above, so it must not close the window too. The launcher's
     * "Esc closes the game" sends D3D12_ESC=close. */
    set_default("D3D12_ESC", "ignore");
}
