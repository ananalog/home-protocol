// Home protocol — C runtime: TLV writer/reader, message header, TCP frames, BLE fragments.
// Allocation-free; used by the generated home_proto.c and by firmware directly.
#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// ---------------------------------------------------------------- writer

typedef struct {
    uint8_t *buf;
    size_t cap;
    size_t len;
    bool overflow;  // set when something did not fit; the result must be discarded
} htlv_writer_t;

void htlv_w_init(htlv_writer_t *w, uint8_t *buf, size_t cap);
void htlv_w_raw(htlv_writer_t *w, const void *data, size_t n);
void htlv_w_u8(htlv_writer_t *w, uint8_t tag, uint8_t v);
void htlv_w_u16(htlv_writer_t *w, uint8_t tag, uint16_t v);
void htlv_w_u32(htlv_writer_t *w, uint8_t tag, uint32_t v);
void htlv_w_u64(htlv_writer_t *w, uint8_t tag, uint64_t v);
void htlv_w_i32(htlv_writer_t *w, uint8_t tag, int32_t v);
void htlv_w_i64(htlv_writer_t *w, uint8_t tag, int64_t v);
void htlv_w_f32(htlv_writer_t *w, uint8_t tag, float v);
void htlv_w_bool(htlv_writer_t *w, uint8_t tag, bool v);
void htlv_w_str(htlv_writer_t *w, uint8_t tag, const char *s);
void htlv_w_bytes(htlv_writer_t *w, uint8_t tag, const uint8_t *data, size_t n);

// Nested struct: begin returns a mark, end fixes up the length (canonical varint).
size_t htlv_w_begin(htlv_writer_t *w, uint8_t tag);
void htlv_w_end(htlv_writer_t *w, size_t mark);

// ---------------------------------------------------------------- reader

typedef struct {
    const uint8_t *p;
    size_t len;
    size_t pos;
} htlv_reader_t;

typedef enum {
    HTLV_END = 0,
    HTLV_OK = 1,
    HTLV_ERROR = -1,
} htlv_result_t;

void htlv_r_init(htlv_reader_t *r, const uint8_t *p, size_t len);
// Returns HTLV_OK with the next field, HTLV_END at the end, HTLV_ERROR on malformed input.
htlv_result_t htlv_r_next(htlv_reader_t *r, uint8_t *tag, const uint8_t **val, size_t *vlen);

bool htlv_get_u8(const uint8_t *v, size_t n, uint8_t *out);
bool htlv_get_u16(const uint8_t *v, size_t n, uint16_t *out);
bool htlv_get_u32(const uint8_t *v, size_t n, uint32_t *out);
bool htlv_get_u64(const uint8_t *v, size_t n, uint64_t *out);
bool htlv_get_i32(const uint8_t *v, size_t n, int32_t *out);
bool htlv_get_i64(const uint8_t *v, size_t n, int64_t *out);
bool htlv_get_f32(const uint8_t *v, size_t n, float *out);
bool htlv_get_bool(const uint8_t *v, size_t n, bool *out);
// Copies a string into dst (cap includes the terminating zero). Fails if it does not fit.
bool htlv_get_str(const uint8_t *v, size_t n, char *dst, size_t cap);

size_t htlv_varint_put(uint8_t *dst, uint32_t v);
size_t htlv_varint_size(uint32_t v);

// ---------------------------------------------------------------- errors of generated decoders

typedef enum {
    HP_DECODE_OK = 0,
    HP_DECODE_MALFORMED = 1,       // broken TLV or wrong scalar size
    HP_DECODE_UNSUPPORTED = 2,     // unknown critical tag
    HP_DECODE_TOO_LONG = 3,        // string/bytes/list larger than the C buffer
} hp_decode_result_t;

// ---------------------------------------------------------------- message header

#define HP_HEADER_SIZE 6

typedef struct {
    uint8_t ver;
    uint8_t type;
    uint8_t flags;
    uint16_t req_id;
} hp_header_t;

void hp_header_write(htlv_writer_t *w, const hp_header_t *h);
bool hp_header_read(const uint8_t *msg, size_t len, hp_header_t *h);

// ---------------------------------------------------------------- CRC and TCP frames
// TCP frame: 'H' 'M' | len u16le (message length) | message | crc16 u16le (CRC-16/CCITT-FALSE of message)

#define HP_FRAME_OVERHEAD 6

uint16_t hp_crc16(const uint8_t *data, size_t n);

// Writes a frame around msg into dst. Returns frame length or 0 if it does not fit.
size_t hp_frame_encode(const uint8_t *msg, size_t msg_len, uint8_t *dst, size_t cap);

typedef enum {
    HP_FRAME_NEED_MORE = 0,  // not enough bytes yet
    HP_FRAME_READY = 1,      // *msg/*msg_len point into buf, *consumed bytes used
    HP_FRAME_SKIP = 2,       // garbage or bad crc: drop *consumed bytes and try again
} hp_frame_result_t;

hp_frame_result_t hp_frame_decode(const uint8_t *buf, size_t len, size_t max_msg,
                                  const uint8_t **msg, size_t *msg_len, size_t *consumed);

// ---------------------------------------------------------------- BLE fragments
// Fragment: hdr u8 (bit7 FIRST, bit6 LAST, bits0-3 seq) | payload.

#define HP_BLE_FIRST 0x80
#define HP_BLE_LAST 0x40

typedef struct {
    uint8_t *buf;
    size_t cap;
    size_t len;
    uint8_t next_seq;
    bool active;
} hp_ble_rx_t;

void hp_ble_rx_init(hp_ble_rx_t *rx, uint8_t *buf, size_t cap);
// Feeds one fragment. Returns true when a whole message is in rx->buf[0..rx->len).
bool hp_ble_rx_feed(hp_ble_rx_t *rx, const uint8_t *frag, size_t n);

// Splits msg into fragments of at most mtu_payload bytes (including the 1-byte header).
// Calls send(frag, n, ctx) for each; stops and returns false if send fails.
typedef bool (*hp_ble_send_fn)(const uint8_t *frag, size_t n, void *ctx);
bool hp_ble_tx(const uint8_t *msg, size_t len, size_t mtu_payload, hp_ble_send_fn send, void *ctx);

#ifdef __cplusplus
}
#endif
