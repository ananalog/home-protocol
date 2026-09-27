package home.protocol

import java.io.ByteArrayOutputStream

/** Body of a message or a nested struct. */
interface ProtoBody {
    fun write(w: TlvWriter)
}

/** A request or response body bound to a message type. */
interface ProtoMessage : ProtoBody {
    val msgType: Int
    val isResponse: Boolean
}

class ProtoException(val code: Int, message: String) : Exception(message)

/** Growable TLV writer; canonical encoding (minimal varints). */
class TlvWriter(capacity: Int = 256) {
    private var buf = ByteArray(maxOf(capacity, 16))
    var length = 0
        private set

    fun toByteArray(): ByteArray = buf.copyOf(length)

    private fun ensure(more: Int) {
        if (length + more > buf.size) buf = buf.copyOf(maxOf(buf.size * 2, length + more))
    }

    fun raw(data: ByteArray, off: Int = 0, len: Int = data.size) {
        ensure(len)
        System.arraycopy(data, off, buf, length, len)
        length += len
    }

    private fun putVarint(v0: Int): Int {
        var v = v0
        var n = 0
        while (v >= 0x80) {
            buf[length + n++] = (v or 0x80).toByte()
            v = v ushr 7
        }
        buf[length + n++] = v.toByte()
        return n
    }

    private fun head(tag: Int, len: Int) {
        ensure(6)
        buf[length++] = tag.toByte()
        length += putVarint(len)
    }

    private fun le(tag: Int, v: Long, n: Int) {
        head(tag, n)
        ensure(n)
        for (i in 0 until n) buf[length++] = (v ushr (8 * i)).toByte()
    }

    fun u8(tag: Int, v: Int) = le(tag, v.toLong() and 0xFF, 1)
    fun u16(tag: Int, v: Int) = le(tag, v.toLong() and 0xFFFF, 2)
    fun u32(tag: Int, v: Long) = le(tag, v and 0xFFFFFFFFL, 4)
    fun u64(tag: Int, v: Long) = le(tag, v, 8)
    fun i32(tag: Int, v: Int) = le(tag, v.toLong() and 0xFFFFFFFFL, 4)
    fun i64(tag: Int, v: Long) = le(tag, v, 8)
    fun f32(tag: Int, v: Float) = le(tag, java.lang.Float.floatToRawIntBits(v).toLong() and 0xFFFFFFFFL, 4)
    fun bool(tag: Int, v: Boolean) = le(tag, if (v) 1 else 0, 1)

    fun str(tag: Int, v: String, max: Int) {
        val b = v.toByteArray(Charsets.UTF_8)
        if (b.size > max) throw ProtoException(ErrorCode.INVALID_VALUE, "string longer than $max bytes")
        head(tag, b.size)
        raw(b)
    }

    fun bytes(tag: Int, v: ByteArray, max: Int) {
        if (v.size > max) throw ProtoException(ErrorCode.INVALID_VALUE, "bytes longer than $max")
        head(tag, v.size)
        raw(v)
    }

    fun struct(tag: Int, body: ProtoBody) {
        ensure(2)
        buf[length++] = tag.toByte()
        buf[length++] = 0
        val start = length
        body.write(this)
        val n = length - start
        val vs = varintSize(n)
        if (vs > 1) {
            ensure(vs - 1)
            System.arraycopy(buf, start, buf, start + vs - 1, n)
            length += vs - 1
        }
        val save = length
        length = start - 1
        putVarint(n)
        length = save
    }

    companion object {
        fun varintSize(v0: Int): Int {
            var v = v0
            var n = 1
            while (v >= 0x80) { v = v ushr 7; n++ }
            return n
        }
    }
}

/** Reads TLV fields; throws [ProtoException] on malformed input. */
class TlvReader(private val data: ByteArray, private var pos: Int = 0, private val end: Int = data.size) {
    var tag = 0
        private set
    var valueOff = 0
        private set
    var valueLen = 0
        private set

    fun next(): Boolean {
        if (pos >= end) return false
        tag = data[pos++].toInt() and 0xFF
        var n = 0
        var shift = 0
        while (true) {
            if (pos >= end || shift > 28) throw ProtoException(ErrorCode.BAD_REQUEST, "bad varint")
            val b = data[pos++].toInt() and 0xFF
            n = n or ((b and 0x7F) shl shift)
            if (b and 0x80 == 0) break
            shift += 7
        }
        if (n < 0 || n > end - pos) throw ProtoException(ErrorCode.BAD_REQUEST, "field exceeds message")
        valueOff = pos
        valueLen = n
        pos += n
        return true
    }

    private fun le(n: Int): Long {
        if (valueLen != n) throw ProtoException(ErrorCode.BAD_REQUEST, "expected $n bytes, got $valueLen")
        var x = 0L
        for (i in 0 until n) x = x or ((data[valueOff + i].toLong() and 0xFF) shl (8 * i))
        return x
    }

    fun u8(): Int = le(1).toInt()
    fun u16(): Int = le(2).toInt()
    fun u32(): Long = le(4)
    fun u64(): Long = le(8)
    fun i32(): Int = le(4).toInt()
    fun i64(): Long = le(8)
    fun f32(): Float = java.lang.Float.intBitsToFloat(le(4).toInt())
    fun bool(): Boolean = le(1) != 0L
    fun str(): String = String(data, valueOff, valueLen, Charsets.UTF_8)
    fun bytes(): ByteArray = data.copyOfRange(valueOff, valueOff + valueLen)
    fun <T> struct(read: (ByteArray, Int, Int) -> T): T = read(data, valueOff, valueOff + valueLen)

    fun unknown() {
        if (tag and 0x80 != 0) throw ProtoException(ErrorCode.UNSUPPORTED, "unknown critical tag ${tag and 0x7F}")
    }
}
