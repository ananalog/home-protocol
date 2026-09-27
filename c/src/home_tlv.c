#include "home_tlv.h"

#include <string.h>

// ---------------------------------------------------------------- writer

void htlv_w_init(htlv_writer_t *w, uint8_t *buf, size_t cap)
{
    w->buf = buf;
    w->cap = cap;
    w->len = 0;
    w->overflow = false;
}

void htlv_w_raw(htlv_writer_t *w, const void *data, size_t n)
{
    if (w->overflow || w->len + n > w->cap) {
        w->overflow = true;
        return;
    }
    if (n) memcpy(w->buf + w->len, data, n);
    w->len += n;
}

size_t htlv_varint_size(uint32_t v)
{
    size_t n = 1;
    while (v >= 0x80) {
        v >>= 7;
        n++;
    }
    return n;
}

size_t htlv_varint_put(uint8_t *dst, uint32_t v)
{
    size_t n = 0;
    while (v >= 0x80) {
        dst[n++] = (uint8_t)(v | 0x80);
        v >>= 7;
    }
    dst[n++] = (uint8_t)v;
    return n;
}

static void w_head(htlv_writer_t *w, uint8_t tag, size_t n)
{
    uint8_t tmp[6];
    tmp[0] = tag;
    size_t k = 1 + htlv_varint_put(tmp + 1, (uint32_t)n);
    htlv_w_raw(w, tmp, k);
}

static void w_le(htlv_writer_t *w, uint8_t tag, uint64_t v, size_t n)
{
    uint8_t tmp[8];
    for (size_t i = 0; i < n; i++) tmp[i] = (uint8_t)(v >> (8 * i));
    w_head(w, tag, n);
    htlv_w_raw(w, tmp, n);
}

void htlv_w_u8(htlv_writer_t *w, uint8_t tag, uint8_t v) { w_le(w, tag, v, 1); }
void htlv_w_u16(htlv_writer_t *w, uint8_t tag, uint16_t v) { w_le(w, tag, v, 2); }
void htlv_w_u32(htlv_writer_t *w, uint8_t tag, uint32_t v) { w_le(w, tag, v, 4); }
void htlv_w_u64(htlv_writer_t *w, uint8_t tag, uint64_t v) { w_le(w, tag, v, 8); }
void htlv_w_i32(htlv_writer_t *w, uint8_t tag, int32_t v) { w_le(w, tag, (uint32_t)v, 4); }
void htlv_w_i64(htlv_writer_t *w, uint8_t tag, int64_t v) { w_le(w, tag, (uint64_t)v, 8); }
void htlv_w_bool(htlv_writer_t *w, uint8_t tag, bool v) { w_le(w, tag, v ? 1 : 0, 1); }

void htlv_w_f32(htlv_writer_t *w, uint8_t tag, float v)
{
    uint32_t u;
    memcpy(&u, &v, 4);
    w_le(w, tag, u, 4);
}

void htlv_w_str(htlv_writer_t *w, uint8_t tag, const char *s)
{
    size_t n = s ? strlen(s) : 0;
    w_head(w, tag, n);
    htlv_w_raw(w, s, n);
}

void htlv_w_bytes(htlv_writer_t *w, uint8_t tag, const uint8_t *data, size_t n)
{
    w_head(w, tag, n);
    htlv_w_raw(w, data, n);
}

size_t htlv_w_begin(htlv_writer_t *w, uint8_t tag)
{
    uint8_t hdr[2] = {tag, 0};  // one byte reserved for the length
    htlv_w_raw(w, hdr, 2);
    return w->len;  // start of the nested content
}

void htlv_w_end(htlv_writer_t *w, size_t mark)
{
    if (w->overflow) return;
    size_t n = w->len - mark;
    size_t vs = htlv_varint_size((uint32_t)n);
    if (vs > 1) {
        if (w->len + vs - 1 > w->cap) {
            w->overflow = true;
            return;
        }
        memmove(w->buf + mark + vs - 1, w->buf + mark, n);
        w->len += vs - 1;
    }
    htlv_varint_put(w->buf + mark - 1, (uint32_t)n);
}

// ---------------------------------------------------------------- reader

void htlv_r_init(htlv_reader_t *r, const uint8_t *p, size_t len)
{
    r->p = p;
    r->len = len;
    r->pos = 0;
}

htlv_result_t htlv_r_next(htlv_reader_t *r, uint8_t *tag, const uint8_t **val, size_t *vlen)
{
    if (r->pos >= r->len) return HTLV_END;
    *tag = r->p[r->pos++];
    uint32_t n = 0;
    int shift = 0;
    for (;;) {
        if (r->pos >= r->len || shift > 28) return HTLV_ERROR;
        uint8_t b = r->p[r->pos++];
        n |= (uint32_t)(b & 0x7F) << shift;
        if (!(b & 0x80)) break;
        shift += 7;
    }
    if (n > r->len - r->pos) return HTLV_ERROR;
    *val = r->p + r->pos;
    *vlen = n;
    r->pos += n;
    return HTLV_OK;
}

static bool get_le(const uint8_t *v, size_t n, size_t want, uint64_t *out)
{
    if (n != want) return false;
    uint64_t x = 0;
    for (size_t i = 0; i < n; i++) x |= (uint64_t)v[i] << (8 * i);
    *out = x;
    return true;
}

bool htlv_get_u8(const uint8_t *v, size_t n, uint8_t *out)
{
    uint64_t x;
    if (!get_le(v, n, 1, &x)) return false;
    *out = (uint8_t)x;
    return true;
}

bool htlv_get_u16(const uint8_t *v, size_t n, uint16_t *out)
{
    uint64_t x;
    if (!get_le(v, n, 2, &x)) return false;
    *out = (uint16_t)x;
    return true;
}

bool htlv_get_u32(const uint8_t *v, size_t n, uint32_t *out)
{
    uint64_t x;
    if (!get_le(v, n, 4, &x)) return false;
    *out = (uint32_t)x;
    return true;
}

bool htlv_get_u64(const uint8_t *v, size_t n, uint64_t *out) { return get_le(v, n, 8, out); }

bool htlv_get_i32(const uint8_t *v, size_t n, int32_t *out)
{
    uint64_t x;
    if (!get_le(v, n, 4, &x)) return false;
    *out = (int32_t)(uint32_t)x;
    return true;
}

bool htlv_get_i64(const uint8_t *v, size_t n, int64_t *out)
{
    uint64_t x;
    if (!get_le(v, n, 8, &x)) return false;
    *out = (int64_t)x;
    return true;
}

bool htlv_get_f32(const uint8_t *v, size_t n, float *out)
{
    uint64_t x;
    if (!get_le(v, n, 4, &x)) return false;
    uint32_t u = (uint32_t)x;
    memcpy(out, &u, 4);
    return true;
}

bool htlv_get_bool(const uint8_t *v, size_t n, bool *out)
{
    uint64_t x;
    if (!get_le(v, n, 1, &x)) return false;
    *out = x != 0;
    return true;
}

bool htlv_get_str(const uint8_t *v, size_t n, char *dst, size_t cap)
{
    if (n + 1 > cap) return false;
    memcpy(dst, v, n);
    dst[n] = 0;
    return true;
}

// ---------------------------------------------------------------- header

void hp_header_write(htlv_writer_t *w, const hp_header_t *h)
{
    uint8_t b[HP_HEADER_SIZE] = {h->ver, h->type, h->flags, 0, (uint8_t)h->req_id, (uint8_t)(h->req_id >> 8)};
    htlv_w_raw(w, b, sizeof b);
}

bool hp_header_read(const uint8_t *msg, size_t len, hp_header_t *h)
{
    if (len < HP_HEADER_SIZE) return false;
    h->ver = msg[0];
    h->type = msg[1];
    h->flags = msg[2];
    h->req_id = (uint16_t)(msg[4] | (msg[5] << 8));
    return true;
}

// ---------------------------------------------------------------- CRC / frames

uint16_t hp_crc16(const uint8_t *data, size_t n)
{
    uint16_t crc = 0xFFFF;
    for (size_t i = 0; i < n; i++) {
        crc ^= (uint16_t)data[i] << 8;
        for (int b = 0; b < 8; b++) crc = (crc & 0x8000) ? (uint16_t)((crc << 1) ^ 0x1021) : (uint16_t)(crc << 1);
    }
    return crc;
}

size_t hp_frame_encode(const uint8_t *msg, size_t msg_len, uint8_t *dst, size_t cap)
{
    if (msg_len > 0xFFFF || msg_len + HP_FRAME_OVERHEAD > cap) return 0;
    uint16_t crc = hp_crc16(msg, msg_len);
    dst[0] = 'H';
    dst[1] = 'M';
    dst[2] = (uint8_t)msg_len;
    dst[3] = (uint8_t)(msg_len >> 8);
    if (dst + 4 != msg) memmove(dst + 4, msg, msg_len);
    dst[4 + msg_len] = (uint8_t)crc;
    dst[5 + msg_len] = (uint8_t)(crc >> 8);
    return msg_len + HP_FRAME_OVERHEAD;
}

hp_frame_result_t hp_frame_decode(const uint8_t *buf, size_t len, size_t max_msg,
                                  const uint8_t **msg, size_t *msg_len, size_t *consumed)
{
    *consumed = 0;
    if (len < 1) return HP_FRAME_NEED_MORE;
    if (buf[0] != 'H') {
        *consumed = 1;
        return HP_FRAME_SKIP;
    }
    if (len < 2) return HP_FRAME_NEED_MORE;
    if (buf[1] != 'M') {
        *consumed = 1;
        return HP_FRAME_SKIP;
    }
    if (len < 4) return HP_FRAME_NEED_MORE;
    size_t n = (size_t)buf[2] | ((size_t)buf[3] << 8);
    if (n < HP_HEADER_SIZE || n > max_msg) {
        *consumed = 1;
        return HP_FRAME_SKIP;
    }
    if (len < n + HP_FRAME_OVERHEAD) return HP_FRAME_NEED_MORE;
    uint16_t crc = (uint16_t)(buf[4 + n] | (buf[5 + n] << 8));
    if (crc != hp_crc16(buf + 4, n)) {
        *consumed = 1;
        return HP_FRAME_SKIP;
    }
    *msg = buf + 4;
    *msg_len = n;
    *consumed = n + HP_FRAME_OVERHEAD;
    return HP_FRAME_READY;
}

// ---------------------------------------------------------------- BLE

void hp_ble_rx_init(hp_ble_rx_t *rx, uint8_t *buf, size_t cap)
{
    rx->buf = buf;
    rx->cap = cap;
    rx->len = 0;
    rx->next_seq = 0;
    rx->active = false;
}

bool hp_ble_rx_feed(hp_ble_rx_t *rx, const uint8_t *frag, size_t n)
{
    if (n < 1) return false;
    uint8_t hdr = frag[0];
    uint8_t seq = hdr & 0x0F;
    if (hdr & HP_BLE_FIRST) {
        rx->len = 0;
        rx->active = true;
    } else if (!rx->active || seq != rx->next_seq) {
        rx->active = false;  // lost fragment: drop the message
        return false;
    }
    if (rx->len + n - 1 > rx->cap) {
        rx->active = false;
        return false;
    }
    memcpy(rx->buf + rx->len, frag + 1, n - 1);
    rx->len += n - 1;
    rx->next_seq = (uint8_t)((seq + 1) & 0x0F);
    if (hdr & HP_BLE_LAST) {
        rx->active = false;
        return true;
    }
    return false;
}

bool hp_ble_tx(const uint8_t *msg, size_t len, size_t mtu_payload, hp_ble_send_fn send, void *ctx)
{
    if (mtu_payload < 2) return false;
    uint8_t frag[512];
    size_t chunk = mtu_payload - 1;
    if (chunk > sizeof frag - 1) chunk = sizeof frag - 1;
    size_t off = 0;
    uint8_t seq = 0;
    do {
        size_t n = len - off < chunk ? len - off : chunk;
        frag[0] = (uint8_t)(seq & 0x0F);
        if (off == 0) frag[0] |= HP_BLE_FIRST;
        if (off + n == len) frag[0] |= HP_BLE_LAST;
        memcpy(frag + 1, msg + off, n);
        if (!send(frag, n + 1, ctx)) return false;
        off += n;
        seq++;
    } while (off < len);
    return true;
}
