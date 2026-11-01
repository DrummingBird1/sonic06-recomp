/*
 * SONIC06_BOOT=stage|story|dev: start in one of the developer modes left in the
 * title, instead of the title screen. They reach every stage, boss and character
 * without the story, which is the only way to test those from a scripted pad.
 * The menu's text does not show: the device's text call is an empty function on
 * PS3 (slot 0xBC of MyGraphicsDevice). tools/re/stagelist.py gives the presses.
 *
 * DocState is a state machine of singletons, and the title boots into
 * StateMainMode. StateStageSelect<Stage>, StateStageSelect<Story> and
 * StateDevTitle are still compiled in, so their enter and update slots go over
 * StateMainMode's. The ELF is loaded after our constructors run and its first
 * state is entered soon after, so a thread waits for the table to appear.
 */
#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

extern uint8_t* vm_base;

#define MAINMODE_VTABLE 0x100A0D58u   /* dtor, dtor, enter, update */
#define MAINMODE_ENTER  0x016289E8u   /* OPD of its enter */

static uint32_t rd(uint32_t ea)
{
    return __builtin_bswap32(*(volatile uint32_t*)(vm_base + ea));
}

static void wr(uint32_t ea, uint32_t v)
{
    *(volatile uint32_t*)(vm_base + ea) = __builtin_bswap32(v);
}

static DWORD WINAPI boot_patch(LPVOID param)
{
    const uint32_t from = (uint32_t)(uintptr_t)param;
    const ULONGLONG end = GetTickCount64() + 30000;
    while (GetTickCount64() < end) {
        if (vm_base && rd(MAINMODE_VTABLE + 8) == MAINMODE_ENTER) {
            wr(MAINMODE_VTABLE + 8, rd(from + 8));
            wr(MAINMODE_VTABLE + 12, rd(from + 12));
            fprintf(stderr, "[boot] state tables patched from 0x%08X\n", from);
            return 0;
        }
        Sleep(0);
    }
    fprintf(stderr, "[boot] StateMainMode table never appeared\n");
    return 0;
}

__attribute__((constructor))
static void sonic06_boot(void)
{
    char v[16] = {0};
    if (!GetEnvironmentVariableA("SONIC06_BOOT", v, sizeof v)) return;
    uint32_t from = 0;
    if (!strcmp(v, "stage")) from = 0x100A0D18u;
    else if (!strcmp(v, "story")) from = 0x100A0C70u;
    else if (!strcmp(v, "dev")) from = 0x100A0C00u;
    if (from) CreateThread(NULL, 0, boot_patch, (LPVOID)(uintptr_t)from, 0, NULL);
}
#endif
