plugins {
    kotlin("jvm") version "2.1.21"
}

group = "home"
version = "1.0.0"

repositories {
    mavenCentral()
}

kotlin {
    jvmToolchain(21)
}

dependencies {
    testImplementation(kotlin("test"))
}

tasks.test {
    useJUnitPlatform()
    systemProperty("vectors", file("../testdata/vectors").absolutePath)
}
