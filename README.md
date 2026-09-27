# home-protocol

Бинарный протокол системы [Home](https://github.com/ananalog/home): схема сообщений, генератор кода,
рантаймы для C / C# / Kotlin и эталонные сообщения (тест-векторы).
Спецификация — [`home/doc/02-protocol.md`](https://github.com/ananalog/home/blob/main/doc/02-protocol.md).

## Структура

```
schema.yaml                  единственный источник правды: сообщения, поля, типы, перечисления, константы
gen/Home.ProtoGen/           генератор (C#): schema.yaml → C, C#, Kotlin, TypeScript
gen/Home.ProtoVectors/       кодирует testdata/vectors.json → testdata/vectors/*.bin
c/                           C-библиотека без malloc: include/home_tlv.h (рантайм), include/home_proto.h (сгенерировано)
csharp/Home.Protocol/        .NET-библиотека: Runtime/ (TLV, кодек, фреймы, BLE) + Generated/Proto.g.cs
kotlin/                      Kotlin/JVM-библиотека для Android: Tlv.kt, Codec.kt + Proto.kt (сгенерировано)
ts/home_proto.ts             константы и перечисления для Mini App
testdata/                    vectors.json (описание) и vectors/*.bin (эталонные байты)
tests/                       тесты C (CMake), C# (xUnit); Kotlin — kotlin/src/test
```

## Работа со схемой

```
./scripts/generate.sh        # после правки schema.yaml: перегенерировать код и тест-векторы
./scripts/test.sh            # прогнать эталонные сообщения через C, C# и Kotlin
./scripts/check-generated.sh # проверка в CI, что сгенерированный код закоммичен
```

Требуется .NET 10 SDK, CMake + GCC, JDK 21.

## Формат (кратко)

- Сообщение: `ver u8 | type u8 | flags u8 | rsvd u8 | req_id u16le` + поля TLV.
- TLV: `tag u8` (бит 7 — критичное поле) `| len varint | value`; числа — little-endian фиксированной ширины;
  `list<T>` — повтор тега; вложенные структуры — TLV внутри TLV.
- TCP: `'H' 'M' | len u16le | сообщение | crc16 (CCITT-FALSE)`.
- BLE: фрагменты `hdr u8 (FIRST|LAST|seq) | данные`.

Кодирование каноническое (поля по возрастанию тега, минимальные varint), поэтому все реализации
выдают одинаковые байты — это и проверяют тест-векторы.

## Правила изменения схемы

- Новые поля и сообщения — только с новыми тегами/ID; это minor-версия (`version.minor`).
- Удаление/изменение смысла поля — major-версия.
- Поле, без понимания которого получатель не может корректно обработать сообщение, помечается `critical: true`.
