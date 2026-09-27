# home-protocol

Бинарный протокол системы [Home](https://github.com/ananalog/home): схема сообщений, генератор кода
и тестовые векторы. Спецификация — [`home/doc/02-protocol.md`](https://github.com/ananalog/home/blob/main/doc/02-protocol.md).

```
schema.yaml        единственный источник правды: сообщения, поля, типы, модели устройств
gen/               генератор (C#): schema.yaml → C, C#, Kotlin, TypeScript
generated/c/       ESP-IDF компонент home_proto
generated/csharp/  Home.Protocol (сервер, CLI, эмулятор)
generated/kotlin/  кодеки для Android
generated/ts/      константы для Mini App
testdata/vectors/  эталонные сообщения (.bin + .json) для проверки всех реализаций
```

Подключается сабмодулем в `home-server`, `home-firmware`, `home-android`.
