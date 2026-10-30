# Sonic the Hedgehog (2006), PS3 BLES00028 -- ps3recomp port.
#
# Same shape as the toolkit's templates/project, with the instrumented boot
# harness (runtime/ppu/tests/boot_main.cpp) as the entry point, like the VF5
# port: every unlifted function, unimplemented import and lv2 syscall logs
# itself, which is what the boot phase runs on.
#
# Everything game-derived is generated into work/ (git-ignored) by
# tools/relift.ps1. Configure from a "x64 Native Tools" environment:
#   cmake -S . -B build -G Ninja -DCMAKE_BUILD_TYPE=Release ^
#         -DCMAKE_C_COMPILER=clang-cl -DCMAKE_CXX_COMPILER=clang-cl
#   cmake --build build
# Run (tools/run.ps1 sets this up):
#   set PS3_VFS_ROOT=game\disc
#   build\sonic06.exe game\EBOOT.elf
cmake_minimum_required(VERSION 3.20)
project(sonic06 VERSION 0.1.0 LANGUAGES C CXX)

set(CMAKE_C_STANDARD 17)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# The project's patched copy of the toolkit (tools/setup_ps3recomp.ps1).
set(PS3RECOMP_DIR "${CMAKE_CURRENT_SOURCE_DIR}/work/ps3recomp" CACHE PATH "ps3recomp checkout")
set(RECOMP_DIR "${CMAKE_CURRENT_SOURCE_DIR}/work/recompiled" CACHE PATH
    "Lifter output: ppu_recomp.h and the lifted sources")
get_filename_component(PS3RECOMP_DIR "${PS3RECOMP_DIR}" ABSOLUTE)
get_filename_component(RECOMP_DIR "${RECOMP_DIR}" ABSOLUTE)

if(NOT EXISTS "${PS3RECOMP_DIR}/CMakeLists.txt")
    message(FATAL_ERROR "No ps3recomp in ${PS3RECOMP_DIR}. Run tools/setup_ps3recomp.ps1 first.")
endif()
if(NOT EXISTS "${RECOMP_DIR}/ppu_recomp.h")
    message(FATAL_ERROR "No ppu_recomp.h in ${RECOMP_DIR}. Run tools/relift.ps1 first.")
endif()
file(GLOB RECOMP_SOURCES CONFIGURE_DEPENDS "${RECOMP_DIR}/*.cpp")
list(LENGTH RECOMP_SOURCES RECOMP_SOURCE_COUNT)
message(STATUS "Lifted sources:  ${RECOMP_SOURCE_COUNT} file(s) in ${RECOMP_DIR}")

add_subdirectory("${PS3RECOMP_DIR}" "${CMAKE_BINARY_DIR}/ps3recomp_sdk" EXCLUDE_FROM_ALL)

# The HLE NID -> handler table for every module the runtime ships.
find_package(Python3 COMPONENTS Interpreter REQUIRED)
set(HLE_NID_TABLE "${CMAKE_CURRENT_BINARY_DIR}/ppu_hle_nids.cpp")
file(GLOB_RECURSE HLE_NID_SOURCES CONFIGURE_DEPENDS "${PS3RECOMP_DIR}/libs/*.c")
add_custom_command(
    OUTPUT "${HLE_NID_TABLE}"
    COMMAND "${Python3_EXECUTABLE}" "${PS3RECOMP_DIR}/tools/gen_hle_nids.py"
            --out "${HLE_NID_TABLE}" --all
    WORKING_DIRECTORY "${PS3RECOMP_DIR}"
    DEPENDS "${PS3RECOMP_DIR}/tools/gen_hle_nids.py" ${HLE_NID_SOURCES}
    COMMENT "Generating the HLE NID -> handler table")

# This port's own code: HLE handlers the toolkit lacks, title fixes.
file(GLOB PORT_SOURCES CONFIGURE_DEPENDS
    "${CMAKE_CURRENT_SOURCE_DIR}/src/*.c"
    "${CMAKE_CURRENT_SOURCE_DIR}/src/*.cpp")

# Lifted SPU programs, generated at configure time from the decrypted EBOOT.
# Needed past the title screen: the main menu waits on an event from the
# FrustumCullingTask SPU threads, and an image missing from the workload
# registry is never started.
option(PORT_ENABLE_SPU "Lift and register the title's SPU programs" ON)
set(SPU_SOURCES "")
if(PORT_ENABLE_SPU)
    include("${PS3RECOMP_DIR}/cmake/PS3RecompSPU.cmake")
    ps3recomp_generate_spu("${CMAKE_CURRENT_BINARY_DIR}/generated_spu"
                           "${CMAKE_CURRENT_SOURCE_DIR}/game/EBOOT.elf")
    list(APPEND SPU_SOURCES ${SPU_GENERATED_SOURCES}
         "${PS3RECOMP_DIR}/runtime/spu/spu_dispatch_mt.c")
endif()

add_executable(sonic06
    "${PS3RECOMP_DIR}/runtime/ppu/tests/boot_main.cpp"
    "${PS3RECOMP_DIR}/runtime/ppu/ppu_loader.cpp"
    "${PS3RECOMP_DIR}/runtime/ppu/ppu_hle.cpp"
    "${PS3RECOMP_DIR}/runtime/ppu/ppu_fs.cpp"
    "${PS3RECOMP_DIR}/runtime/ppu/ppu_sysprx.cpp"
    ${HLE_NID_TABLE}
    ${PORT_SOURCES}
    ${RECOMP_SOURCES}
    ${SPU_SOURCES}
)

if(SPU_SOURCES)
    set_source_files_properties(${SPU_SOURCES} PROPERTIES
        INCLUDE_DIRECTORIES "${PS3RECOMP_DIR}/runtime/spu"
        COMPILE_DEFINITIONS SPU_USE_DISPATCH_MT
        COMPILE_OPTIONS "-O2")
endif()

target_include_directories(sonic06 PRIVATE
    "${RECOMP_DIR}"
    "${PS3RECOMP_DIR}/include"
    "${PS3RECOMP_DIR}/runtime/ppu"
    "${PS3RECOMP_DIR}/runtime/spu"
    "${PS3RECOMP_DIR}/runtime/memory"
    "${PS3RECOMP_DIR}/runtime/syscalls"
    "${PS3RECOMP_DIR}/runtime/platform"
    "${PS3RECOMP_DIR}/libs/video"
    "${CMAKE_CURRENT_SOURCE_DIR}/src"
)
target_link_libraries(sonic06 PRIVATE ps3recomp_runtime)
set_target_properties(sonic06 PROPERTIES LINKER_LANGUAGE CXX)

if(WIN32)
    target_link_libraries(sonic06 PRIVATE dbghelp user32 gdi32 winmm)
    # Every guest call is a host call, so recompiled call chains nest deeply.
    # The map names lifted functions in the runtime's host backtraces
    # (WATCHDOG, TOCWATCH and friends print RVAs).
    target_link_options(sonic06 PRIVATE /STACK:268435456 /MAP:sonic06.map)
endif()

# The lift spells PPC arithmetic as signed C arithmetic, which must wrap.
target_compile_options(sonic06 PRIVATE -fno-strict-aliasing -fwrapv)
if(MSVC)   # clang-cl, where -Wall means -Weverything
    target_compile_options(sonic06 PRIVATE /W3)
    target_compile_definitions(sonic06 PRIVATE _CRT_SECURE_NO_WARNINGS)
    # Lifted units are machine-generated and pass the default COFF section limit.
    set_source_files_properties(${RECOMP_SOURCES} PROPERTIES COMPILE_OPTIONS "/w;/bigobj")
else()
    target_compile_options(sonic06 PRIVATE -Wall -Wextra -Wno-unused-parameter)
    set_source_files_properties(${RECOMP_SOURCES} PROPERTIES COMPILE_OPTIONS "-w;-ffp-contract=off")
endif()
