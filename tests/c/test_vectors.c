// Decodes every test vector with the C implementation, re-encodes it and compares bytes.
// Also checks framing (TCP) and BLE fragmentation round trips.
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "home_proto.h"

int hp_test_roundtrip(uint8_t type, int kind, const uint8_t *body, size_t n, htlv_writer_t *w);

static uint8_t *read_file(const char *path, size_t *n)
{
    FILE *f = fopen(path, "rb");
    if (!f) return NULL;
    fseek(f, 0, SEEK_END);
    long sz = ftell(f);
    fseek(f, 0, SEEK_SET);
    uint8_t *buf = malloc((size_t)sz + 1);
    *n = fread(buf, 1, (size_t)sz, f);
    fclose(f);
    return buf;
}

typedef struct {
    uint8_t buf[4096];
    size_t len;
} sink_t;

static bool sink_send(const uint8_t *frag, size_t n, void *ctx)
{
    hp_ble_rx_t *rx = ctx;
    return hp_ble_rx_feed(rx, frag, n) || !(frag[0] & HP_BLE_LAST);
}

static int check_transport(const uint8_t *msg, size_t n)
{
    static uint8_t frame[4200];
    size_t fl = hp_frame_encode(msg, n, frame + 3, sizeof frame - 3);
    frame[0] = 0x00, frame[1] = 'H', frame[2] = 0x13;  // garbage in front
    const uint8_t *out;
    size_t out_len, used, off = 0;
    for (;;) {
        hp_frame_result_t r = hp_frame_decode(frame + off, fl + 3 - off, HP_MAX_MESSAGE, &out, &out_len, &used);
        if (r == HP_FRAME_READY) break;
        if (r != HP_FRAME_SKIP) return 1;
        off += used;
    }
    if (out_len != n || memcmp(out, msg, n) != 0) return 2;

    static uint8_t rxbuf[4096];
    hp_ble_rx_t rx;
    hp_ble_rx_init(&rx, rxbuf, sizeof rxbuf);
    if (!hp_ble_tx(msg, n, 20, sink_send, &rx)) return 3;
    if (rx.len != n || memcmp(rxbuf, msg, n) != 0) return 4;
    return 0;
}

int main(int argc, char **argv)
{
    const char *dir = argc > 1 ? argv[1] : "testdata/vectors";
    char path[512];
    snprintf(path, sizeof path, "%s/index.txt", dir);
    FILE *idx = fopen(path, "r");
    if (!idx) {
        fprintf(stderr, "cannot open %s\n", path);
        return 1;
    }
    char name[128], kind[16];
    unsigned type;
    int failed = 0, total = 0;
    while (fscanf(idx, "%127s %u %15s", name, &type, kind) == 3) {
        total++;
        snprintf(path, sizeof path, "%s/%s.bin", dir, name);
        size_t n;
        uint8_t *msg = read_file(path, &n);
        hp_header_t h;
        if (!msg || !hp_header_read(msg, n, &h) || h.type != type) {
            printf("FAIL %s: bad file/header\n", name);
            failed++;
            free(msg);
            continue;
        }
        int k = strcmp(kind, "err") == 0 ? 2 : strcmp(kind, "resp") == 0 ? 1 : 0;
        static uint8_t out[4096];
        htlv_writer_t w;
        htlv_w_init(&w, out, sizeof out);
        hp_header_write(&w, &h);
        int rc = hp_test_roundtrip(h.type, k, msg + HP_HEADER_SIZE, n - HP_HEADER_SIZE, &w);
        if (rc != 0 || w.overflow || w.len != n || memcmp(out, msg, n) != 0) {
            printf("FAIL %s: rc=%d len=%zu/%zu\n", name, rc, w.len, n);
            failed++;
        } else if ((rc = check_transport(msg, n)) != 0) {
            printf("FAIL %s: transport %d\n", name, rc);
            failed++;
        } else {
            printf("ok   %s\n", name);
        }
        free(msg);
    }
    fclose(idx);

    // Unknown critical tag must be rejected, unknown optional tag skipped.
    const uint8_t crit[] = {0x01, 0x01, 0x05, 0xFF, 0x01, 0x00};
    const uint8_t opt[] = {0x01, 0x01, 0x05, 0x7F, 0x01, 0x00};
    hp_set_req_t sr;
    if (hp_set_req_read(&sr, crit, sizeof crit) != HP_DECODE_UNSUPPORTED) { printf("FAIL critical tag\n"); failed++; }
    if (hp_set_req_read(&sr, opt, sizeof opt) != HP_DECODE_OK || sr.point != 5) { printf("FAIL optional tag\n"); failed++; }

    printf("%d/%d vectors passed\n", total - failed, total);
    return failed ? 1 : 0;
}
