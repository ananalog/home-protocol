package home.protocol

data class Header(val ver: Int, val type: Int, val flags: Int, val reqId: Int) {
    val isResponse get() = flags and Flags.RESP != 0
    val isError get() = flags and Flags.ERR != 0

    fun toBytes(): ByteArray = byteArrayOf(ver.toByte(), type.toByte(), flags.toByte(), 0, reqId.toByte(), (reqId ushr 8).toByte())

    companion object {
        const val SIZE = 6
        fun read(msg: ByteArray): Header {
            if (msg.size < SIZE) throw ProtoException(ErrorCode.BAD_REQUEST, "message shorter than header")
            return Header(
                msg[0].toInt() and 0xFF, msg[1].toInt() and 0xFF, msg[2].toInt() and 0xFF,
                (msg[4].toInt() and 0xFF) or ((msg[5].toInt() and 0xFF) shl 8),
            )
        }
    }
}

/** A decoded message; body is [ErrorBody] when the ERR flag is set. */
data class Message(val header: Header, val body: ProtoBody)

object Codec {
    fun encode(body: ProtoMessage, reqId: Int, extraFlags: Int = 0): ByteArray =
        encode(body.msgType, extraFlags or (if (body.isResponse) Flags.RESP else 0), reqId, body)

    fun encode(type: Int, flags: Int, reqId: Int, body: ProtoBody): ByteArray {
        val w = TlvWriter()
        w.raw(Header(Proto.HEADER_VERSION, type, flags, reqId).toBytes())
        body.write(w)
        if (w.length > Proto.MAX_MESSAGE) throw ProtoException(ErrorCode.INVALID_VALUE, "message too large")
        return w.toByteArray()
    }

    fun decode(msg: ByteArray): Message {
        val h = Header.read(msg)
        val off = Header.SIZE
        val body: ProtoBody = when {
            h.isError -> ErrorBody.read(msg, off, msg.size)
            h.isResponse -> Messages.readResponse(h.type, msg, off, msg.size)
            else -> Messages.readRequest(h.type, msg, off, msg.size)
        } ?: throw ProtoException(ErrorCode.UNSUPPORTED, "unknown message type ${h.type}")
        return Message(h, body)
    }
}

object Crc16 {
    /** CRC-16/CCITT-FALSE (poly 0x1021, init 0xFFFF). */
    fun compute(data: ByteArray, off: Int = 0, len: Int = data.size): Int {
        var crc = 0xFFFF
        for (i in off until off + len) {
            crc = crc xor ((data[i].toInt() and 0xFF) shl 8)
            repeat(8) { crc = if (crc and 0x8000 != 0) ((crc shl 1) xor 0x1021) and 0xFFFF else (crc shl 1) and 0xFFFF }
        }
        return crc
    }
}

/** BLE fragmentation: hdr u8 (bit7 FIRST, bit6 LAST, bits0-3 seq) | payload. */
object BleFragments {
    const val FIRST = 0x80
    const val LAST = 0x40

    fun split(msg: ByteArray, mtuPayload: Int): List<ByteArray> {
        val chunk = mtuPayload - 1
        require(chunk >= 1)
        val out = ArrayList<ByteArray>()
        var off = 0
        var seq = 0
        do {
            val n = minOf(chunk, msg.size - off)
            val f = ByteArray(n + 1)
            var hdr = seq and 0x0F
            if (off == 0) hdr = hdr or FIRST
            if (off + n == msg.size) hdr = hdr or LAST
            f[0] = hdr.toByte()
            System.arraycopy(msg, off, f, 1, n)
            out += f
            off += n
            seq++
        } while (off < msg.size)
        return out
    }
}

class BleReassembler(private val maxMessage: Int = Proto.MAX_MESSAGE) {
    private val buf = java.io.ByteArrayOutputStream()
    private var nextSeq = 0
    private var active = false

    fun feed(frag: ByteArray): ByteArray? {
        if (frag.isEmpty()) return null
        val hdr = frag[0].toInt() and 0xFF
        val seq = hdr and 0x0F
        if (hdr and BleFragments.FIRST != 0) {
            buf.reset()
            active = true
        } else if (!active || seq != nextSeq) {
            active = false
            return null
        }
        if (buf.size() + frag.size - 1 > maxMessage) {
            active = false
            return null
        }
        buf.write(frag, 1, frag.size - 1)
        nextSeq = (seq + 1) and 0x0F
        if (hdr and BleFragments.LAST == 0) return null
        active = false
        return buf.toByteArray()
    }
}
