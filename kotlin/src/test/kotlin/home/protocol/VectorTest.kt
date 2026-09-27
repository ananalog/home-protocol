package home.protocol

import java.io.File
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertTrue

class VectorTest {
    private val dir = File(System.getProperty("vectors") ?: "../testdata/vectors")

    @Test
    fun allVectorsRoundTrip() {
        val names = File(dir, "index.txt").readLines().filter { it.isNotBlank() }.map { it.split(' ')[0] }
        assertTrue(names.size >= 20)
        for (name in names) {
            val bytes = File(dir, "$name.bin").readBytes()
            val msg = Codec.decode(bytes)
            val again = Codec.encode(msg.header.type, msg.header.flags, msg.header.reqId, msg.body)
            assertContentEquals(bytes, again, "vector $name")
        }
    }

    @Test
    fun helloFields() {
        val msg = Codec.decode(File(dir, "hello_req.bin").readBytes())
        val hello = msg.body as HelloReq
        assertEquals("a1b2c3d4e5f6", hello.deviceId)
        assertEquals(Model.CO2_EGG, hello.model)
        assertEquals("Спальня CO2", hello.name)
    }

    @Test
    fun bleFragments() {
        val bytes = File(dir, "ota_data_req.bin").readBytes()
        val rx = BleReassembler()
        var got: ByteArray? = null
        for (f in BleFragments.split(bytes, 20)) got = rx.feed(f) ?: got
        assertContentEquals(bytes, got)
    }

    @Test
    fun unknownCriticalTag() {
        assertFailsWith<ProtoException> { SetReq.read(byteArrayOf(0x01, 0x01, 0x05, 0xFF.toByte(), 0x01, 0x00)) }
        assertEquals(5, SetReq.read(byteArrayOf(0x01, 0x01, 0x05, 0x7F, 0x01, 0x00)).point)
    }

    @Test
    fun crc() = assertEquals(0x29B1, Crc16.compute("123456789".toByteArray()))
}
