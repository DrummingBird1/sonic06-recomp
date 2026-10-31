/*
 * cellDmux: the PAMF demultiplexer behind every movie in the title (the SEGA
 * logo, the title loop, the event and ending movies). It replaces
 * ps3recomp's module of the same name, which calls the title's guest
 * callbacks as host function pointers, hands back host pointers and never
 * splits the stream into access units.
 *
 * What the title sees follows the SDK behaviour as RPCS3 reverse-engineered it
 * (cellDmux.cpp and cellDmuxPamf.cpp there):
 *
 *   - The handles are addresses in the memory the title gave cellDmuxOpen and
 *     cellDmuxEnableEs, sized by cellDmuxQueryAttr / cellDmuxQueryEsAttr with
 *     the same numbers as the real library.
 *   - Each elementary stream has an AU queue of auQueueMaxSize + 1 slots (one
 *     kept back for the AU a flush produces) and a data buffer that the AUs
 *     are copied into, both inside the ES memory. cellDmuxGetAu hands out the
 *     oldest unread AU; cellDmuxReleaseAu frees the oldest allocated one.
 *   - Demuxing runs on its own thread, as libdmux's SPU task and callback
 *     thread do, and calls the title's callbacks from there: AU_FOUND for
 *     every AU, DEMUX_DONE once the stream set by cellDmuxSetStream is used
 *     up (or reset), FLUSH_DONE after cellDmuxFlushEs. A full AU queue
 *     stalls the demuxer until the title releases AUs.
 *
 * Only the two stream types this title's movies carry are parsed: AVC video
 * (an AU runs from one access unit delimiter to the next) and ATRAC3plus
 * audio (an AU is one frame: 0x0FD0 sync word, size in the next two bytes,
 * 8-byte header included). Each 2048-byte pack holds one PES packet.
 */
#include "ppu_context.h"

#include <algorithm>
#include <condition_variable>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <deque>
#include <memory>
#include <mutex>
#include <thread>
#include <vector>

typedef void (*hle_ctx_fn)(ppu_context*);
extern "C" void ps3_hle_register_ctx(uint32_t nid, const char* name, hle_ctx_fn fn);
extern "C" uint64_t ppu_guest_call(uint32_t opd, uint64_t a0, uint64_t a1, uint64_t a2, uint64_t a3,
                                   uint64_t a4, uint64_t a5, uint64_t a6, uint64_t a7);

/* ppu_memory.h is C only (C11 atomics), so this file uses the runtime's
 * out-of-line accessors, as ppu_hle.cpp and the lifted code do. */
extern "C" {
extern uint8_t* vm_base;
uint8_t vm_read8(uint64_t addr);
uint32_t vm_read32(uint64_t addr);
void vm_write8(uint64_t addr, uint8_t val);
void vm_write32(uint64_t addr, uint32_t val);
void vm_write64(uint64_t addr, uint64_t val);
}

namespace {

uint8_t* vm_ptr8(uint32_t addr) { return vm_base + addr; }

constexpr uint32_t CELL_OK = 0;
constexpr uint32_t CELL_DMUX_ERROR_ARG = 0x80610201u;
constexpr uint32_t CELL_DMUX_ERROR_SEQ = 0x80610202u;
constexpr uint32_t CELL_DMUX_ERROR_BUSY = 0x80610203u;
constexpr uint32_t CELL_DMUX_ERROR_EMPTY = 0x80610204u;

constexpr int32_t CELL_DMUX_STREAM_TYPE_PAMF = 1;
constexpr int32_t CELL_DMUX_MSG_TYPE_DEMUX_DONE = 0;
constexpr int32_t CELL_DMUX_MSG_TYPE_PROG_END_CODE = 2;
constexpr int32_t CELL_DMUX_ES_MSG_TYPE_AU_FOUND = 0;
constexpr int32_t CELL_DMUX_ES_MSG_TYPE_FLUSH_DONE = 1;

/* Sizes of the real library's structures, so the memory the title is told
 * to allocate matches what it was tuned against. */
constexpr uint32_t DMUX_CONTEXT_SIZE = 0xC0;       /* DmuxContext */
constexpr uint32_t ES_CONTEXT_SIZE = 0x50;         /* DmuxEsContext */
constexpr uint32_t AU_ELEMENT_SIZE = 0x38;         /* DmuxAuQueueElement */
constexpr uint32_t PAMF_MAX_ENABLED_ES = 0x40;
constexpr uint32_t PAMF_CORE_MEM_SIZE = 0x1C + 0x3D880 + 0xE7B;   /* handle + context + slack */
constexpr uint32_t PAMF_ES_CORE_SIZE = 0x98;       /* DmuxPamfElementaryStream */
constexpr uint32_t PACK_SIZE = 0x800;

/* Offsets in our guest-side areas. */
constexpr uint32_t DMUX_MSG = 0x20;                /* CellDmuxMsg handed to the demuxer callback */
constexpr uint32_t ES_MSG = 0x20;                  /* CellDmuxEsMsg handed to the ES callback */

/* A DmuxAuQueueElement holds a DmuxAuInfo at +8: CellDmuxAuInfo (auAddr,
 * auSize, auMaxSize, isRap, userData, pts, dts) then the specific info. */
constexpr uint32_t EL_INFO = 0x08;

constexpr uint64_t TS_INVALID = ~0ull;

uint32_t align_up(uint32_t v, uint32_t a) { return (v + a - 1) & ~(a - 1); }

uint16_t be16(const uint8_t* p) { return (uint16_t)(p[0] << 8 | p[1]); }
uint32_t be32(const uint8_t* p) { return (uint32_t)p[0] << 24 | (uint32_t)p[1] << 16 | (uint32_t)p[2] << 8 | p[3]; }

/* The 33-bit PES timestamp at p. Bit 32 is dropped, as libdmux does; this
 * title's movies never get near it. */
uint64_t pes_timestamp(const uint8_t* p)
{
    return ((uint64_t)(p[0] >> 1 & 7) << 30 | (uint64_t)(be16(p + 1) >> 1) << 15 | (uint64_t)(be16(p + 3) >> 1))
           & 0xFFFFFFFFu;
}

/* Per-stream-type numbers of the real PAMF demuxer. */
struct EsKind {
    bool avc;              /* otherwise ATRAC3plus */
    uint32_t queue_max;    /* auQueueMaxSize */
    uint32_t spec_size;    /* AU specific info size */
    uint32_t buffer_size;  /* AU data buffer */
    uint32_t au_max;       /* largest AU */
};

/* filterIdMajor / filterIdMinor / supplementalInfo1 -> stream kind. Returns
 * false for the stream types this port does not demux. */
bool es_kind(uint32_t major, uint32_t minor, uint32_t is_avc, uint32_t spec_info, EsKind* k)
{
    if ((major & 0xF0) == 0xE0 && is_avc) {
        /* CellDmuxPamfEsSpecificInfoAvc.level, or the 4.2 sizes when absent */
        uint32_t level = spec_info ? vm_read32(spec_info) : 42;
        k->avc = true;
        k->queue_max = 0x40;
        k->spec_size = 4;
        switch (level) {
        case 21: k->buffer_size = 0xB00C0;  k->au_max = 0x12900; break;
        case 30: k->buffer_size = 0x19F2E0; k->au_max = 0x25F80; break;
        case 31: k->buffer_size = 0x260120; k->au_max = 0x54600; break;
        case 32: k->buffer_size = 0x35F6C0; k->au_max = 0x78000; break;
        case 41: k->buffer_size = 0x45E870; k->au_max = 0xC0000; break;
        default: k->buffer_size = 0x46A870; k->au_max = 0xCC000; break;
        }
        return true;
    }
    if (major == 0xBD && minor < 0x10) {
        k->avc = false;
        k->queue_max = 0x40;
        k->spec_size = 0;
        k->buffer_size = 0x6400;
        k->au_max = 0x1008;
        return true;
    }
    return false;
}

/* CellDmuxEsAttr.memSize for a stream kind (the real library's formula). */
uint32_t es_mem_size(const EsKind& k)
{
    uint32_t core = k.spec_size * k.queue_max + k.buffer_size + 0x7F + PAMF_ES_CORE_SIZE + 0xF;
    return align_up(ES_CONTEXT_SIZE + (k.queue_max + 1) * (k.spec_size + AU_ELEMENT_SIZE), 0x10) + core + 0xF;
}

struct HostAu {
    std::vector<uint8_t> data;
    uint64_t pts, dts;
    bool rap;
};

struct Es {
    uint32_t handle = 0;
    uint32_t stream_id = 0, private_id = 0;
    EsKind kind{};
    uint32_t cb_func = 0, cb_arg = 0;

    /* The AU queue, with libdmux's indices: front/size count the AUs the
     * title has not read yet, allocated_back/allocated_size the ones it has
     * not released yet. */
    uint32_t elements = 0, n = 0;
    uint32_t front = 0, back = 0, allocated_back = 0;
    int32_t size = 0, allocated_size = 0;
    std::vector<uint32_t> au_addr, au_size;   /* per slot, host copies */
    uint32_t buf_start = 0, buf_end = 0;

    bool flush_requested = false;
    bool flushing = false;                    /* may use the reserved slot */

    /* The AU being cut out, and the timestamps of the PES packet whose next
     * AU has not started yet. */
    std::vector<uint8_t> cur;
    bool in_au = false;
    uint32_t window = ~0u;
    uint32_t au_target = 0;
    uint64_t pes_pts = TS_INVALID, pes_dts = TS_INVALID;
    bool pes_rap = false;
    uint64_t au_pts = TS_INVALID, au_dts = TS_INVALID;
    bool au_rap = false;

    std::deque<HostAu> ready;                 /* complete, waiting for queue space */

    void discard_partial()
    {
        cur.clear();
        in_au = false;
        window = ~0u;
        au_target = 0;
        pes_pts = pes_dts = TS_INVALID;
        pes_rap = false;
    }

    void begin_au()
    {
        au_pts = pes_pts;
        au_dts = pes_dts;
        au_rap = pes_rap;
        pes_pts = pes_dts = TS_INVALID;
        pes_rap = false;
        in_au = true;
    }

    void finish_au(std::vector<uint8_t>&& data)
    {
        if (!data.empty())
            ready.push_back({std::move(data), au_pts, au_dts, au_rap});
    }

    void feed_avc(const uint8_t* p, size_t len)
    {
        for (size_t i = 0; i < len; i++) {
            window = window << 8 | p[i];
            if (in_au)
                cur.push_back(p[i]);
            if (window != 0x00000109u)
                continue;
            /* An access unit delimiter: the AU before it is complete, and the
             * next one starts with these four bytes. */
            if (in_au) {
                std::vector<uint8_t> done(cur.begin(), cur.end() - 4);
                finish_au(std::move(done));
            }
            cur.assign({0x00, 0x00, 0x01, 0x09});
            begin_au();
        }
    }

    void feed_atrac3plus(const uint8_t* p, size_t len)
    {
        for (size_t i = 0; i < len; i++) {
            if (!in_au) {
                window = window << 8 | p[i];
                if ((window & 0xFFFF) == 0x0FD0) {
                    cur.assign({0x0F, 0xD0});
                    au_target = 0;
                    begin_au();
                }
                continue;
            }
            cur.push_back(p[i]);
            if (cur.size() == 4) {
                uint32_t v = be16(cur.data() + 2) & 0x3FF;
                if (v >= 0x200) {   /* not a frame header after all: resync */
                    discard_partial();
                    continue;
                }
                au_target = (v + 1) * 8 + 8;
            }
            if (au_target && cur.size() == au_target) {
                finish_au(std::move(cur));
                cur.clear();
                in_au = false;
                window = ~0u;
            }
        }
    }
};

struct Dmux {
    uint32_t handle = 0;
    uint32_t cb_func = 0, cb_arg = 0;

    std::mutex m;
    std::condition_variable cv;
    std::thread thread;
    bool quit = false;

    bool running = false;          /* a stream is set and not used up yet */
    bool reset_requested = false;
    uint32_t stream_addr = 0, stream_size = 0, stream_pos = 0;
    uint64_t user_data = 0;

    std::vector<std::unique_ptr<Es>> es;

    void worker();
    bool try_push(Es& e, HostAu& au);
    uint32_t ring_alloc(Es& e, uint32_t size) const;
    bool process_pack(const uint8_t* p, uint32_t len);
    void call_dmux_cb(std::unique_lock<std::mutex>& lk, int32_t type);
    void call_es_cb(std::unique_lock<std::mutex>& lk, Es& e, int32_t type);
};

std::mutex g_lock;                          /* guards g_dmux */
std::vector<std::unique_ptr<Dmux>> g_dmux;

Dmux* find_dmux(uint32_t handle)
{
    std::lock_guard<std::mutex> lk(g_lock);
    for (auto& d : g_dmux)
        if (d->handle == handle)
            return d.get();
    return nullptr;
}

/* The ES behind an ES handle, and its demuxer. */
Es* find_es(uint32_t handle, Dmux** owner)
{
    std::lock_guard<std::mutex> lk(g_lock);
    for (auto& d : g_dmux)
        for (auto& e : d->es)
            if (e->handle == handle) {
                *owner = d.get();
                return e.get();
            }
    return nullptr;
}

uint32_t Dmux::ring_alloc(Es& e, uint32_t size) const
{
    if (e.allocated_size == 0)
        return e.buf_start;
    uint32_t head = e.au_addr[e.allocated_back];
    uint32_t newest = (e.back + e.n - 1) % e.n;
    uint32_t tail = e.au_addr[newest] + e.au_size[newest];
    if (tail > head) {   /* in use: [head, tail) */
        if (e.buf_end - tail >= size) return tail;
        if (head - e.buf_start >= size) return e.buf_start;
        return 0;
    }
    /* wrapped, in use: [head, end) and [start, tail) */
    return head - tail >= size ? tail : 0;
}

/* Copies a finished AU into the ES buffer and queues it. False while the
 * queue or the buffer has no room. */
bool Dmux::try_push(Es& e, HostAu& au)
{
    uint32_t limit = e.n - (e.flushing ? 0 : 1);
    if ((uint32_t)e.allocated_size >= limit)
        return false;
    uint32_t len = (uint32_t)au.data.size();
    uint32_t addr = ring_alloc(e, len);
    if (!addr)
        return false;

    memcpy(vm_ptr8(addr), au.data.data(), len);
    uint32_t el = e.elements + e.back * AU_ELEMENT_SIZE;
    vm_write32(el + EL_INFO + 0x00, addr);
    vm_write32(el + EL_INFO + 0x04, len);
    vm_write32(el + EL_INFO + 0x08, e.kind.au_max);
    vm_write8(el + EL_INFO + 0x0C, au.rap ? 1 : 0);
    vm_write64(el + EL_INFO + 0x10, user_data);
    vm_write32(el + EL_INFO + 0x18, (uint32_t)(au.pts >> 32));
    vm_write32(el + EL_INFO + 0x1C, (uint32_t)au.pts);
    vm_write32(el + EL_INFO + 0x20, (uint32_t)(au.dts >> 32));
    vm_write32(el + EL_INFO + 0x24, (uint32_t)au.dts);
    for (uint32_t i = 0; i < e.kind.spec_size; i++)
        vm_write8(vm_read32(el + 0x30) + i, 0);   /* the AVC specific info is a reserved word */

    e.au_addr[e.back] = addr;
    e.au_size[e.back] = len;
    e.back = (e.back + 1) % e.n;
    e.allocated_size++;
    e.size++;
    return true;
}

void Dmux::call_dmux_cb(std::unique_lock<std::mutex>& lk, int32_t type)
{
    uint32_t msg = handle + DMUX_MSG, func = cb_func, arg = cb_arg, h = handle;
    vm_write32(msg, (uint32_t)type);
    vm_write64(msg + 8, user_data);
    lk.unlock();
    ppu_guest_call(func, h, msg, arg, 0, 0, 0, 0, 0);
    lk.lock();
}

void Dmux::call_es_cb(std::unique_lock<std::mutex>& lk, Es& e, int32_t type)
{
    uint32_t msg = e.handle + ES_MSG, func = e.cb_func, arg = e.cb_arg, eh = e.handle, h = handle;
    vm_write32(msg, (uint32_t)type);
    vm_write64(msg + 8, user_data);
    lk.unlock();
    ppu_guest_call(func, h, eh, msg, arg, 0, 0, 0, 0);
    lk.lock();   /* e may be gone now: callers re-scan instead of using it */
}

/* One pack: pack header, optional system header and private stream 2 (which
 * marks the next video AU as a random access point), then one PES packet.
 * Returns true at a program end code. */
bool Dmux::process_pack(const uint8_t* p, uint32_t len)
{
    if (len < 4)
        return false;
    uint32_t code = be32(p);
    if (code == 0x000001B9u)
        return true;
    if (code != 0x000001BAu || len < 14)
        return false;

    uint32_t i = 14 + (p[13] & 7);
    if (i + 6 <= len && be32(p + i) == 0x000001BBu) {
        i += 6 + be16(p + i + 4);
        if (i + 8 <= len && be32(p + i) == 0x000001BFu) {
            uint32_t ch = be16(p + i + 6) & 0x0F;
            for (auto& e : es)
                if (e->kind.avc && e->stream_id == (0xE0u | ch))
                    e->pes_rap = true;
            i += 6 + be16(p + i + 4);
        }
    }
    if (i + 9 > len || (be32(p + i) >> 8) != 1)
        return false;

    uint32_t sid = p[i + 3];
    uint32_t pes_end = std::min(len, i + 6 + be16(p + i + 4));
    uint32_t data = i + 9 + p[i + 8];
    if (data >= pes_end)
        return false;
    uint32_t private_id = sid == 0xBD ? p[data] : 0;

    for (auto& e : es) {
        if (e->stream_id != sid || (sid == 0xBD && e->private_id != private_id))
            continue;
        uint8_t flags = p[i + 7];
        if (flags & 0x80) e->pes_pts = pes_timestamp(p + i + 9);
        if (flags & 0x40) e->pes_dts = pes_timestamp(p + i + 14);
        if (e->kind.avc) {
            e->feed_avc(p + data, pes_end - data);
        } else if (pes_end - data > 4) {
            /* private stream header: id, then three bytes libdmux skips */
            e->feed_atrac3plus(p + data + 4, pes_end - data - 4);
        }
        break;
    }
    return false;
}

void Dmux::worker()
{
    std::unique_lock<std::mutex> lk(m);
    for (;;) {
        if (quit)
            return;

        if (reset_requested) {
            for (auto& e : es) {
                e->discard_partial();
                e->ready.clear();
            }
            stream_pos = stream_size;
            reset_requested = false;
        }

        /* A flush turns the AU being cut out into a last AU. */
        Es* flush = nullptr;
        for (auto& e : es)
            if (e->flush_requested) { flush = e.get(); break; }
        if (flush) {
            if (flush->in_au)
                flush->finish_au(std::move(flush->cur));
            flush->discard_partial();
            flush->flush_requested = false;
            flush->flushing = true;
            continue;
        }

        /* Hand finished AUs to the title, one callback at a time. */
        bool stalled = false;
        Es* pushed = nullptr;
        for (auto& e : es) {
            if (e->ready.empty())
                continue;
            if (try_push(*e, e->ready.front())) {
                e->ready.pop_front();
                pushed = e.get();
                break;
            }
            stalled = true;
        }
        if (pushed) {
            call_es_cb(lk, *pushed, CELL_DMUX_ES_MSG_TYPE_AU_FOUND);
            continue;
        }
        Es* flushed = nullptr;
        for (auto& e : es)
            if (e->flushing && e->ready.empty()) { flushed = e.get(); break; }
        if (flushed) {
            flushed->flushing = false;
            call_es_cb(lk, *flushed, CELL_DMUX_ES_MSG_TYPE_FLUSH_DONE);
            continue;
        }
        if (stalled) {   /* until the title releases AUs */
            cv.wait(lk);
            continue;
        }

        if (running) {
            if (stream_pos < stream_size) {
                uint32_t len = std::min(PACK_SIZE, stream_size - stream_pos);
                bool prog_end = process_pack(vm_ptr8(stream_addr + stream_pos), len);
                stream_pos += PACK_SIZE;
                if (prog_end)
                    call_dmux_cb(lk, CELL_DMUX_MSG_TYPE_PROG_END_CODE);
                continue;
            }
            running = false;
            call_dmux_cb(lk, CELL_DMUX_MSG_TYPE_DEMUX_DONE);
            continue;
        }
        cv.wait(lk);
    }
}

void ret(ppu_context* ctx, uint32_t rc)
{
    ctx->gpr[3] = (uint64_t)(int64_t)(int32_t)rc;
}

uint32_t arg(ppu_context* ctx, int n) { return (uint32_t)ctx->gpr[3 + n]; }

/* cellDmuxQueryAttr(const CellDmuxType*, CellDmuxAttr*) */
void hle_cellDmuxQueryAttr(ppu_context* ctx)
{
    uint32_t type = arg(ctx, 0), attr = arg(ctx, 1);
    if (!type || !attr || (int32_t)vm_read32(type) != CELL_DMUX_STREAM_TYPE_PAMF) {
        ret(ctx, CELL_DMUX_ERROR_ARG);
        return;
    }
    vm_write32(attr + 0, align_up(DMUX_CONTEXT_SIZE + PAMF_MAX_ENABLED_ES * 4 + ES_CONTEXT_SIZE, 0x10)
                         + PAMF_CORE_MEM_SIZE + 0xF);
    vm_write32(attr + 4, 0x260000);   /* demuxerVerUpper */
    vm_write32(attr + 8, 0x280000);   /* demuxerVerLower: the PAMF core's version */
    ret(ctx, CELL_OK);
}

/* cellDmuxOpen(const CellDmuxType*, const CellDmuxResource*, const CellDmuxCb*,
 * CellDmuxHandle*) */
void hle_cellDmuxOpen(ppu_context* ctx)
{
    uint32_t type = arg(ctx, 0), res = arg(ctx, 1), cb = arg(ctx, 2), out = arg(ctx, 3);
    if (!type || !res || !cb || !out || (int32_t)vm_read32(type) != CELL_DMUX_STREAM_TYPE_PAMF
        || !vm_read32(res) || !vm_read32(cb)) {
        ret(ctx, CELL_DMUX_ERROR_ARG);
        return;
    }
    auto d = std::make_unique<Dmux>();
    d->handle = align_up(vm_read32(res), 0x10);
    d->cb_func = vm_read32(cb);
    d->cb_arg = vm_read32(cb + 4);
    for (uint32_t i = 0; i < DMUX_CONTEXT_SIZE; i += 4)
        vm_write32(d->handle + i, 0);
    vm_write32(d->handle, d->handle);   /* _this, as libdmux keeps it */

    Dmux* raw = d.get();
    {
        std::lock_guard<std::mutex> lk(g_lock);
        g_dmux.push_back(std::move(d));
    }
    raw->thread = std::thread([raw] { raw->worker(); });
    vm_write32(out, raw->handle);
    printf("[cellDmux] Open -> 0x%08X (mem 0x%08X)\n", raw->handle, vm_read32(res));
    ret(ctx, CELL_OK);
}

/* cellDmuxClose(CellDmuxHandle) */
void hle_cellDmuxClose(ppu_context* ctx)
{
    uint32_t handle = arg(ctx, 0);
    std::unique_ptr<Dmux> d;
    {
        std::lock_guard<std::mutex> lk(g_lock);
        auto it = std::find_if(g_dmux.begin(), g_dmux.end(), [&](auto& x) { return x->handle == handle; });
        if (it == g_dmux.end()) {
            ret(ctx, CELL_DMUX_ERROR_ARG);
            return;
        }
        d = std::move(*it);
        g_dmux.erase(it);
    }
    {
        std::lock_guard<std::mutex> lk(d->m);
        d->quit = true;
    }
    d->cv.notify_all();
    if (d->thread.get_id() == std::this_thread::get_id())
        d->thread.detach();   /* closed from one of its own callbacks */
    else
        d->thread.join();
    vm_write32(handle, 0);
    printf("[cellDmux] Close 0x%08X\n", handle);
    ret(ctx, CELL_OK);
}

/* cellDmuxSetStream(CellDmuxHandle, const void* addr, u32 size,
 * b8 discontinuity, u64 userData) */
void hle_cellDmuxSetStream(ppu_context* ctx)
{
    Dmux* d = find_dmux(arg(ctx, 0));
    uint32_t addr = arg(ctx, 1), size = arg(ctx, 2);
    bool discontinuity = (ctx->gpr[6] & 0xFF) != 0;
    if (!d || size == 0 || size == ~0u) {
        ret(ctx, CELL_DMUX_ERROR_ARG);
        return;
    }
    {
        std::lock_guard<std::mutex> lk(d->m);
        if (d->running) {
            ret(ctx, CELL_DMUX_ERROR_BUSY);
            return;
        }
        if (discontinuity)
            for (auto& e : d->es)
                e->discard_partial();
        d->stream_addr = addr;
        d->stream_size = size;
        d->stream_pos = 0;
        d->user_data = ctx->gpr[7];
        d->running = true;
    }
    d->cv.notify_all();
    ret(ctx, CELL_OK);
}

/* cellDmuxResetStream(CellDmuxHandle): drop the rest of the stream; the
 * demuxer then reports DEMUX_DONE. */
void hle_cellDmuxResetStream(ppu_context* ctx)
{
    Dmux* d = find_dmux(arg(ctx, 0));
    if (!d) {
        ret(ctx, CELL_DMUX_ERROR_ARG);
        return;
    }
    {
        std::lock_guard<std::mutex> lk(d->m);
        if (!d->running) {
            ret(ctx, CELL_DMUX_ERROR_SEQ);
            return;
        }
        d->reset_requested = true;
    }
    d->cv.notify_all();
    printf("[cellDmux] ResetStream 0x%08X\n", d->handle);
    ret(ctx, CELL_OK);
}

/* cellDmuxQueryEsAttr(const CellDmuxType*, const CellCodecEsFilterId*,
 * const void* esSpecificInfo, CellDmuxEsAttr*) */
void hle_cellDmuxQueryEsAttr(ppu_context* ctx)
{
    uint32_t type = arg(ctx, 0), filter = arg(ctx, 1), spec = arg(ctx, 2), attr = arg(ctx, 3);
    EsKind k;
    if (!type || !filter || !attr || (int32_t)vm_read32(type) != CELL_DMUX_STREAM_TYPE_PAMF
        || !es_kind(vm_read32(filter), vm_read32(filter + 4), vm_read32(filter + 8), spec, &k)) {
        ret(ctx, CELL_DMUX_ERROR_ARG);
        return;
    }
    vm_write32(attr, es_mem_size(k));
    ret(ctx, CELL_OK);
}

/* cellDmuxEnableEs(CellDmuxHandle, const CellCodecEsFilterId*,
 * const CellDmuxEsResource*, const CellDmuxEsCb*, const void* esSpecificInfo,
 * CellDmuxEsHandle*) */
void hle_cellDmuxEnableEs(ppu_context* ctx)
{
    Dmux* d = find_dmux(arg(ctx, 0));
    uint32_t filter = arg(ctx, 1), res = arg(ctx, 2), cb = arg(ctx, 3), spec = arg(ctx, 4), out = arg(ctx, 5);
    EsKind k;
    if (!d || !filter || !res || !cb || !out || !vm_read32(res) || !vm_read32(cb)) {
        ret(ctx, CELL_DMUX_ERROR_ARG);
        return;
    }
    uint32_t major = vm_read32(filter), minor = vm_read32(filter + 4);
    if (!es_kind(major, minor, vm_read32(filter + 8), spec, &k)) {
        printf("[cellDmux] EnableEs: stream 0x%02X/0x%02X is not demuxed by this port\n", major, minor);
        ret(ctx, CELL_DMUX_ERROR_ARG);
        return;
    }
    uint32_t mem = vm_read32(res), mem_size = vm_read32(res + 4);
    if (mem_size < es_mem_size(k)) {
        ret(ctx, CELL_DMUX_ERROR_ARG);
        return;
    }

    auto e = std::make_unique<Es>();
    e->handle = align_up(mem, 0x10);
    e->stream_id = major;
    e->private_id = major == 0xBD ? minor : 0;
    e->kind = k;
    e->cb_func = vm_read32(cb);
    e->cb_arg = vm_read32(cb + 4);
    e->n = k.queue_max + 1;
    e->elements = e->handle + ES_CONTEXT_SIZE;
    e->au_addr.assign(e->n, 0);
    e->au_size.assign(e->n, 0);
    uint32_t spec_area = e->elements + e->n * AU_ELEMENT_SIZE;
    e->buf_start = align_up(spec_area + e->n * k.spec_size, 0x80);
    e->buf_end = std::min(e->buf_start + k.buffer_size, mem + mem_size);

    for (uint32_t i = 0; i < ES_CONTEXT_SIZE; i += 4)
        vm_write32(e->handle + i, 0);
    for (uint32_t i = 0; i < e->n; i++) {
        uint32_t el = e->elements + i * AU_ELEMENT_SIZE;
        for (uint32_t j = 0; j < AU_ELEMENT_SIZE; j += 4)
            vm_write32(el + j, 0);
        vm_write32(el, i);
        vm_write32(el + 0x30, spec_area + i * k.spec_size);
        vm_write32(el + 0x34, k.spec_size);
    }

    uint32_t handle = e->handle;
    {
        std::lock_guard<std::mutex> lk(d->m);
        d->es.push_back(std::move(e));
    }
    vm_write32(out, handle);
    printf("[cellDmux] EnableEs 0x%02X/0x%02X -> 0x%08X (%s, buffer 0x%X, memory 0x%08X+0x%X)\n",
           major, minor, handle, k.avc ? "AVC" : "ATRAC3plus", k.buffer_size, mem, mem_size);
    ret(ctx, CELL_OK);
}

/* cellDmuxDisableEs(CellDmuxEsHandle) */
void hle_cellDmuxDisableEs(ppu_context* ctx)
{
    Dmux* d = nullptr;
    uint32_t handle = arg(ctx, 0);
    if (!find_es(handle, &d)) {
        ret(ctx, CELL_DMUX_ERROR_ARG);
        return;
    }
    {
        std::lock_guard<std::mutex> lk(d->m);
        d->es.erase(std::remove_if(d->es.begin(), d->es.end(), [&](auto& e) { return e->handle == handle; }),
                    d->es.end());
    }
    d->cv.notify_all();
    ret(ctx, CELL_OK);
}

/* cellDmuxResetEs(CellDmuxEsHandle): empties the AU queue. Only while the
 * demuxer is stopped. */
void hle_cellDmuxResetEs(ppu_context* ctx)
{
    Dmux* d = nullptr;
    Es* e = find_es(arg(ctx, 0), &d);
    if (!e) {
        ret(ctx, CELL_DMUX_ERROR_ARG);
        return;
    }
    {
        std::lock_guard<std::mutex> lk(d->m);
        if (d->running) {
            ret(ctx, CELL_DMUX_ERROR_SEQ);
            return;
        }
        e->front = e->back = e->allocated_back = 0;
        e->size = e->allocated_size = 0;
        e->ready.clear();
        e->discard_partial();
    }
    d->cv.notify_all();
    ret(ctx, CELL_OK);
}

/* cellDmuxGetAu(CellDmuxEsHandle, const CellDmuxAuInfo** auInfo,
 * const void** auSpecificInfo) */
void hle_cellDmuxGetAu(ppu_context* ctx)
{
    Dmux* d = nullptr;
    Es* e = find_es(arg(ctx, 0), &d);
    uint32_t info_out = arg(ctx, 1), spec_out = arg(ctx, 2);
    if (!e) {
        ret(ctx, CELL_DMUX_ERROR_ARG);
        return;
    }
    std::lock_guard<std::mutex> lk(d->m);
    if (e->size <= 0) {
        ret(ctx, CELL_DMUX_ERROR_EMPTY);
        return;
    }
    uint32_t el = e->elements + e->front * AU_ELEMENT_SIZE;
    if (info_out) vm_write32(info_out, el + EL_INFO);
    if (spec_out) vm_write32(spec_out, vm_read32(el + 0x30));
    e->front = (e->front + 1) % e->n;
    e->size--;
    ret(ctx, CELL_OK);
}

/* cellDmuxReleaseAu(CellDmuxEsHandle): frees the oldest AU still held. */
void hle_cellDmuxReleaseAu(ppu_context* ctx)
{
    Dmux* d = nullptr;
    Es* e = find_es(arg(ctx, 0), &d);
    if (!e) {
        ret(ctx, CELL_DMUX_ERROR_ARG);
        return;
    }
    {
        std::lock_guard<std::mutex> lk(d->m);
        if (e->allocated_size < 1) {
            ret(ctx, CELL_DMUX_ERROR_SEQ);
            return;
        }
        e->allocated_back = (e->allocated_back + 1) % e->n;
        e->allocated_size--;
        if (e->allocated_size < e->size) {   /* released one that was never read */
            e->front = (e->front + 1) % e->n;
            e->size--;
        }
    }
    d->cv.notify_all();
    ret(ctx, CELL_OK);
}

/* cellDmuxFlushEs(CellDmuxEsHandle): emits the AU still being cut out, then
 * FLUSH_DONE. Only while the demuxer is stopped. */
void hle_cellDmuxFlushEs(ppu_context* ctx)
{
    Dmux* d = nullptr;
    Es* e = find_es(arg(ctx, 0), &d);
    if (!e) {
        ret(ctx, CELL_DMUX_ERROR_ARG);
        return;
    }
    {
        std::lock_guard<std::mutex> lk(d->m);
        if (d->running) {
            ret(ctx, CELL_DMUX_ERROR_SEQ);
            return;
        }
        e->flush_requested = true;
    }
    d->cv.notify_all();
    ret(ctx, CELL_OK);
}

struct Register {
    Register()
    {
        ps3_hle_register_ctx(0xA2D4189Bu, "cellDmuxQueryAttr", hle_cellDmuxQueryAttr);
        ps3_hle_register_ctx(0x68492DE9u, "cellDmuxOpen", hle_cellDmuxOpen);
        ps3_hle_register_ctx(0x8C692521u, "cellDmuxClose", hle_cellDmuxClose);
        ps3_hle_register_ctx(0x04E7499Fu, "cellDmuxSetStream", hle_cellDmuxSetStream);
        ps3_hle_register_ctx(0x5D345DE9u, "cellDmuxResetStream", hle_cellDmuxResetStream);
        ps3_hle_register_ctx(0x02170D1Au, "cellDmuxQueryEsAttr", hle_cellDmuxQueryEsAttr);
        ps3_hle_register_ctx(0x7B56DC3Fu, "cellDmuxEnableEs", hle_cellDmuxEnableEs);
        ps3_hle_register_ctx(0x05371C8Du, "cellDmuxDisableEs", hle_cellDmuxDisableEs);
        ps3_hle_register_ctx(0x21D424F0u, "cellDmuxResetEs", hle_cellDmuxResetEs);
        ps3_hle_register_ctx(0x42C716B5u, "cellDmuxGetAu", hle_cellDmuxGetAu);
        ps3_hle_register_ctx(0x24EA6474u, "cellDmuxReleaseAu", hle_cellDmuxReleaseAu);
        ps3_hle_register_ctx(0xEBB3B2BDu, "cellDmuxFlushEs", hle_cellDmuxFlushEs);
    }
} g_register;

}  // namespace
