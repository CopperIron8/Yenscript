# Yenscript

Встраиваемый типизированный язык, компилирующий `.ys` в **core WebAssembly**.
Ядро написано на F# / .NET 9, CLI - на C# / .NET 9.

Пайплайн: `.ys → Parser → проверка типов и lowering → WASM IR → WAT → wat2wasm → .wasm + .meta.json`.
Core не читает файловую систему и не запускает процессы: WABT вызывает CLI.

## Пример

```yenscript
using package system;
from system import *;

fn factorial(n: int): int {
    if (n <= 1) { return 1; }
    else { return n * factorial(n - 1); }
}

fn main() {
    var result: int = factorial(5);
    console:print(strings:concat("result=", convert:intToString(result)));
    for (var i: int = 0; i < 3; i = i + 1) {
        console:warning("iteration");
    }
}
```

Точка входа - обязательная `fn main()` без параметров, с типом `void`.
Объявления пакетов и функций находятся снаружи, исполняемые операторы - только внутри функций.
Хост явно вызывает экспорт `_start` один раз; WASM start-секции нет.

## Сборка компилятора

Нужен SDK .NET 9 либо совместимый более новый SDK. Для managed-запуска нужен runtime .NET 9.

```sh
dotnet restore Yenscript.sln
dotnet build Yenscript.sln -c Release -warnaserror
dotnet test Yenscript.sln
dotnet tool restore
dotnet fantomas src/Yenscript.Core --check
dotnet fantomas tests/Yenscript.Core.Tests --check
```

## Компиляция программы

Установите [WABT](https://github.com/WebAssembly/wabt) с `wat2wasm` в PATH.
Проверяемая версия toolchain - WABT 1.0.42. Это внешняя зависимость **компиляции**,
но готовому WASM-модулю WABT и .NET не нужны.

```sh
dotnet run --project src/Yenscript.Cli -- build examples/hello.ys -o artifacts/hello.wasm
dotnet run --project src/Yenscript.Cli -- check examples/hello.ys
```

После Native AOT-публикации:

```sh
yenscriptc build examples/hello.ys -o hello.wasm
yenscriptc build examples/hello.ys -o hello.wasm --wat2wasm /path/to/wat2wasm
yenscriptc check examples/hello.ys
```

Результат: `hello.wasm`, `hello.wat`, `hello.meta.json`.
`check` не записывает файлы и не требует WABT. Ошибки содержат код и позицию.
Коды выхода: 0 - успех, 1 - ошибка. До успешной сборки и создания metadata предыдущий
output не заменяется. Три итоговых файла не являются общей атомарной транзакцией.

## Пакеты

`system` встроен; `console` - объект этого пакета, не отдельный пакет.
Остальные `.ysp` загружает CLI или встраивающее приложение:

```yenscript
using package custom as c;
using package "../packages/tools.ysp" as tools;
using package "C:/Users/Me/Documents/host.ysp" as host;
from tools import *;
```

Имя ищется рядом с `.ys` как `custom.ysp`; относительный путь считается от исходника,
зависимости - от файла пакета. Абсолютные пути разрешены. При выполнении в недоверенной
среде ограничивайте доступ resolver-а на стороне хоста.
Пакет объявляет host API, но сам не поставляет реализацию: хост связывает WASM imports.

## Исполнение и встраивание

Модуль экспортирует `memory`, `alloc(i32)->i32`, `free(i32,i32)` и `_start()`.
Нужны три функции `env`: `console_print`, `console_warning`, `console_error`,
каждая принимает UTF-8 `(ptr, len)`. `console:error` не завершает программу.
Другие imports определяются подключёнными пакетами.

`wasmtime run hello.wasm` сам по себе недостаточен: обычный CLI не предоставляет
наши `env.console_*`. Используйте host-приложение или SDK. Проверенный Python-хост:

```sh
python -I -m pip install wasmtime==38.0.0
python -I scripts/run-wasm.py hello.wasm
```

Хост ограничивает fuel, не подключает WASI, перехватывает traps и использует `.meta.json`
для поиска позиции исходника. Пример Rust/Wasmtime: [embedding](docs/embedding.md).
Линковка дополнительных native-функций - ответственность приложения.

Чистый API для .NET:

```fsharp
Core.compile source // Result<string, Diagnostic list>: WAT
Core.compileWithPackages (Map.ofList ["../packages/host.ysp", packageText]) source
```

Второй API не читает файлы. Ключи - точные references из исходника/зависимостей.
Публичный API возвращает WAT, а не выдаёт текст за бинарный WASM.

## Документация

- [Язык](docs/language.md)
- [Пакеты и system](docs/packages.md)
- [ABI, source maps и встраивание](docs/embedding.md)
- [Roadmap](roadmap.md)

## Проверки

Unit-тесты покрывают парсер, проверки типов, IR/emission, manifests, resolver и metadata.
Сквозные fixtures компилируются реальным WABT и выполняются Wasmtime с ограничением fuel:
условия/циклы, short-circuit, рекурсия, строки, таблицы, native-объекты, внешние пакеты,
стандартная библиотека и trap с source map.

```sh
python -I -m pip install wasmtime==38.0.0
bash scripts/wasm-integration.sh
bash scripts/publish.sh win-x64  # на Windows с C++ Build Tools
bash scripts/publish.sh linux-x64 # на Linux с clang и zlib1g-dev
```

Компилятор публикуется Native AOT для Windows/Linux x64. WABT не встроен в этот бинарник.
WASM backend использует i32, UTF-8 и линейную память; GC и автоматический сборщик
недостижимых строк/таблиц не реализованы. Подробные ограничения изложены в ABI-документе.
Новый backend несовместим с прежними опубликованными версиями: старые теги не обновлялись.

## Лицензия

[MIT License](LICENSE). Copyright (c) 2026 CopperIron8.
Лицензии WABT, Wasmtime и NuGet-зависимостей сохраняются отдельно.
