plugins {
    kotlin("jvm") version "2.1.21"
}

group = "home"
version = "1.0.0"

repositories {
    mavenCentral()
}

// JDK 21 builds it; the bytecode targets Java 17 so that Android (D8) can use the library.
kotlin {
    jvmToolchain(21)
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
    }
}

java {
    sourceCompatibility = JavaVersion.VERSION_17
    targetCompatibility = JavaVersion.VERSION_17
}

dependencies {
    testImplementation(kotlin("test"))
}

tasks.test {
    useJUnitPlatform()
    systemProperty("vectors", file("../testdata/vectors").absolutePath)
}
