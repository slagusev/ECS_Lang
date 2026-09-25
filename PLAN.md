# Архитектурный мастер-план: ECS-ориентированный язык программирования

## 1. Сводка утвержденных архитектурных решений

| Компонент | Решение | Описание |
|---|---|---|
| **Синтаксис** | Rust + Odin hybrid | Лаконичный, строгий, с ключевыми словами `component`, `system`, `resource`, `query`, `fn`, `pipeline`, `stage`, `commands` |
| **ECS Модель памяти** | Archetype-based (SoA) | Сущности с одинаковым набором компонентов объединяются в архетипы. Данные хранятся непрерывными массивами (столбцами) в чанках для SIMD-векторизации и кеш-локальности |
| **Мутации мира** | Command Buffer (Отложенные) | Добавление/удаление компонентов и спавн сущностей буферизируются (`cmd.spawn(...)`, `cmd.despawn(e)`) и безопасно накатываются на барьерах синхронизации (`sync`) |
| **Глобальные данные** | `resource` (Синглтоны) | Уникальные ресурсы уровня мира (`resource Time`, `resource Input`), доступные системам по ссылке или мутабельно (`mut res: Time`) |
| **Оркестрация** | Явные стадии (Pipeline Stages) | Конвейер с именованными стадиями (`Startup`, `Update`, `Render`) и блоками `parallel { ... }` / `sync` |
| **Компилятор** | C# (.NET 9) + LLVMSharp / libLLVM | Фронтенд (Lexer, Parser, AST, Semantic Type Checker) + LLVM IR Generator |
| **Таргет линковки** | MSVC `link.exe` / `lld-link` | Автопоиск Visual Studio SDK через `vswhere` -> генерация нативного `.exe` (PE/COFF x64) |
| **Первый рабочий рубеж** | **MVP: Hello World** | Сквозная цепочка: компиляция `fn main()` с вызовом `printf`/`puts` через LLVMSharp в рабочий нативный `.exe` |

---

## 2. Спецификация языка (Синтаксис)

### 2.1. Компоненты и Ресурсы
```rust
// Чистые структуры данных
component Position {
    x: f32,
    y: f32,
    z: f32,
}

component Velocity {
    vx: f32,
    vy: f32,
    vz: f32,
}

// Глобальный синглтон-ресурс
resource Time {
    delta_time: f32,
    elapsed: f64,
}
```

### 2.2. Системы, Запросы и Буфер команд
```rust
system MovementSystem {
    // Чтение ресурса Time, мутабельный доступ к Position, чтение Velocity
    query(mut pos: Position, vel: Velocity, time: Time) {
        pos.x += vel.vx * time.delta_time;
        pos.y += vel.vy * time.delta_time;
        pos.z += vel.vz * time.delta_time;
    }
}

system SpawnerSystem {
    // Доступ к буферу команд для отложенных структурных изменений
    query(cmd: Commands) {
        let e = cmd.spawn();
        cmd.add(e, Position { x: 0.0, y: 0.0, z: 0.0 });
        cmd.add(e, Velocity { vx: 1.0, vy: 2.0, vz: 0.0 });
    }
}
```

### 2.3. Пайплайн выполнения
```rust
pipeline MainGameLoop {
    stage Startup {
        SpawnerSystem;
    }

    stage Update {
        parallel {
            MovementSystem;
            // Другие независимые системы
        }
        sync; // Накатывание буфера команд и барьер синхронизации
    }
}
```

---

## 3. Пошаговая дорожная карта реализации

### [x] Этап 0: Инициализация решения C# (.NET 9) и настройка LLVMSharp
- [x] Создание `ECSLang.sln` и модульных проектов (`Core`, `Frontend`, `Semantics`, `Codegen.LLVM`, `Toolchain`, `CLI`).
- [x] Подключение NuGet-пакетов `LLVMSharp 20.1.2`, `libLLVM 20.1.2`, `libLLVM.runtime.win-x64 20.1.2`.
- [x] Настройка x86_64 инициализации LLVM и целевой машины (`x86_64-pc-windows-msvc`).

### [x] Этап 1: MVP "Hello World" (Проверка сквозного цикла)
- **Цель**: собрать и запустить первый нативный `.exe` (Выполнено!).
- **Входной код**:
  ```rust
  fn main(): i32 {
      println("Hello from ECS-Lang via LLVM 20 and MSVC!");
      return 0;
  }
  ```
- **Результаты**:
  1. [x] Базовый Lexer: ключевые слова `fn`, `return`, идентификаторы, строки, скобки, числа.
  2. [x] Базовый Parser: парсинг объявления функции `FunctionDeclaration`, тела с выражениями `CallExpression` и `ReturnStatement`.
  3. [x] LLVM IR Codegen:
     - Декларация внешней функции CRT (`puts`).
     - Создание функции `@main` с блоком `entry`.
     - Генерация строковых констант.
     - Эмиссия вызова функции и `ret i32 0`.
  4. [x] Интеграция с линкером:
     - Сохранение объектного файла `.obj` через `targetMachine.TryEmitToFile`.
     - Автопоиск `link.exe` и Windows SDK Lib через `vswhere.exe`.
     - Линковка с точкой входа `mainCRTStartup` и CRT-библиотеками (`libcmt.lib`, `libucrt.lib` и др.).
     - Успешный запуск `hello.exe` и вывод в консоль.

### [x] Этап 2: Расширение языка (Базовые типы, выражения, переменные, ветвления)
- [x] Примитивные типы (`i32`, `i64`, `f32`, `f64`, `bool`, `string`).
- [x] Переменные: `let x = ...` и `let mut y = ...`.
- [x] Операторы: арифметика (`+`, `-`, `*`, `/`, `%`), составные присваивания (`+=`, `-=`, `*=`, `/=`), сравнения (`==`, `!=`, `<`, `>`, `<=`, `>=`), логические (`&&`, `||`, `!`).
- [x] Управляющие конструкции: `if / else`, `while`.
- [x] Встроенный вывод: `print(...)` и `println(...)` со строками, целыми, вещественными числами и булевыми значениями.
- [x] Проверено сквозным тестом `examples/variables_math.ecs` с компиляцией в рабочий `.exe`.

### [x] Этап 3: Декларации ECS (Парсинг и Семантика)
- [x] Парсинг `component Name { field: type, ... }`.
- [x] Парсинг `resource Name { field: type, ... }`.
- [x] Парсинг `system Name { query(...) { ... } }`.
- [x] Парсинг `pipeline Name { stage StageName { ... } }`.
- [x] Семантический анализатор:
  - [x] Проверка уникальности полей и типов компонентов.
  - [x] Проверка параметров `query`: предотвращение конфликтов алиасинга (`mut` vs чтение).
  - [x] Проверка стадий конвейера и разрешения систем.

### [x] Этап 4: ECS Runtime в машинном коде (Archetype Engine)
- [x] Реализация SoA хранилища архетипов (Archetype Storage):
  - [x] Структуры компонентов (`struct.Position`, `struct.Velocity`).
  - [x] Архетипы: непрерывные массивы по столбцам (`arch_col_Position`, `arch_col_Velocity`), динамическое расширение через CRT `realloc`.
  - [x] Генерация кода итерации: компилятор разворачивает `query(mut pos: Position, vel: Velocity, time: Time)` в плотный цикл с прямым доступом по указателям (Data-Oriented Design).
  - [x] Генерация функций `world_spawn()`, сеттеров компонентов и ресурсов.

### [x] Этап 5: Пайплайны, Оркестрация и CLI
- [x] Генерация функций пайплайна (`pipeline_GameLoop`), последовательный вызов стадий и систем.
- [x] Вызовы пайплайнов из `fn main()`.
- [x] Консольная утилита `ECSLang.CLI`: команды `build`, `run`, флаги `-o`, `--emit-ir`.
- [x] Проверено сквозным тестом симуляции `examples/ecs_simulation.ecs` с компиляцией в машинный код Windows x64 `.exe`.

### [x] Этап 6: Демонстрационный проект "100 000 частиц"
- [x] Проверка производительности: массовый спавн 100 000 сущностей с компонентами `Position` и `Velocity` в Archetype SoA хранилище.
- [x] 10 кадров симуляции физики движения в скомпилированном нативном `.exe` файле [`examples/particles_100k.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/particles_100k.ecs).
- [x] Успешное выполнение с кодом возврата 0 без утечек и переполнения стека.

### [x] Этап 7: Декларативный GUI, тип string и иерархия сущностей
- [x] Поддержка типа `string`:
  - Использование в компонентах (`component TextWidget { content: string, font_size: f32 }`).
  - LLVM-представление через `ptr`, передача строк в сеттеры и загрузка при обращении к полям.
- [x] Встроенная иерархия Parent-Child:
  - Встроенный компонент `ChildOf { parent: i32 }`.
- [x] Топологическая сортировка иерархии в машинном коде:
  - Функция `world_sort_hierarchy()`: вычисление глубины вложенности и синхронная перестановка элементов во всех SoA-колонках архетипа.
  - Поддержка действия `sort_hierarchy;` в стадиях пайплайна (`stage Layout { sort_hierarchy; ... }`).
- [x] Проверено сквозным тестом [`examples/gui_hierarchy.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/gui_hierarchy.ecs): дочерние элементы, созданные раньше родителей, автоматически отсортированы в правильном порядке (Window -> Button -> Label).

### [x] Этап 8: Интерактивный консольный ввод (wait_key / readln)
- [x] Добавлена декларация стандартной Си-функции CRT `declare i32 @getchar()` в LLVM IR генератор.
- [x] Поддержка встроенных функций `wait_key()` и `readln()` в семантическом анализаторе (`TypeChecker`) и компиляторе (`LlvmCodeGenerator`).
- [x] Добавлен алиас `--emit-llvm` наряду с `--emit-ir` в CLI компилятора.
- [x] Обновлены все примеры (`hello.ecs`, `variables_math.ecs`, `ecs_simulation.ecs`, `particles_100k.ecs`, `gui_hierarchy.ecs`): добавлено приглашение `"Press Enter to exit..."` и вызов `wait_key();` перед `return 0;`.

