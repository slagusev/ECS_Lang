# Архитектурный мастер-план: ECS-ориентированный язык программирования

## 1. Сводка утвержденных архитектурных решений

| Компонент | Решение | Описание |
|---|---|---|
| **Синтаксис** | Rust + Odin hybrid | Лаконичный, строгий, с ключевыми словами `component`, `system`, `resource`, `query`, `fn`, `pipeline`, `stage`, `commands` |
| **ECS Контексты** | Изолированные миры (`World`) | ECS — это первоклассная изолированная структура данных (`let mut world = ecs::create_world()`). Поддержка множества независимых миров (Game, UI, Physics) в одном процессе без глобального состояния |
| **ECS Модель памяти** | Archetype-based (SoA) | Сущности с одинаковым набором компонентов объединяются в архетипы. Данные хранятся непрерывными массивами (столбцами) в чанках для SIMD-векторизации и кеш-локальности |
| **Мутации мира** | Методы мира и Command Buffer | Прямые мутации через методы мира (`world.spawn()`, `world.set_*()`, `world.add_*()`, `world.remove_*()`, `world.has_*()`) и отложенные через Command Buffer на барьерах |
| **Ресурсы (Синглтоны)** | Ресурсы уровня инстанса мира (`resource Time`) | Встроены в структуру соответствующего мира, передаются в системы автоматически через контекст мира |
| **Оркестрация** | Явные стадии (Pipeline Stages) | Конвейер с именованными стадиями (`Startup`, `Update`, `Render`) и блоками `parallel { ... }` / `sync`, принимающий целевой контекст мира (`MyPipeline(world)`) |
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

// Ресурс, изолированный внутри конкретного инстанса мира
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
```

### 2.3. Пайплайн выполнения
```rust
pipeline MainGameLoop {
    stage Update {
        MovementSystem;
    }
}
```

### 2.4. Инициализация и исполнение изолированных миров
```rust
fn main(): i32 {
    // Создание изолированного игрового мира
    let mut game_world = ecs::create_world();
    game_world.set_Time(0.016, 0.0);

    let player = game_world.spawn();
    game_world.set_Position(player, 0.0, 0.0, 0.0);
    game_world.set_Velocity(player, 1.0, 2.0, 3.0);

    // Запуск пайплайна над конкретным миром
    MainGameLoop(game_world);
    return 0;
}
```
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
- [x] Обновлены все примеры (`hello.ecs`, `variables_math.ecs`, `ecs_simulation.ecs`, `particles_100k.ecs`, `gui_hierarchy.ecs`, `multi_archetype.ecs`, `multi_world.ecs`): добавлено приглашение `"Press Enter to exit..."` и вызов `wait_key();` перед `return 0;`.
- [x] В кодогенератор внедрен автоматический предохранитель (`ContainsWaitKey`): если пользователь забыл вызвать `wait_key()` в `main()`, компилятор автоматически вставляет печать `"Press Enter to exit..."` и вызов Си-функции `getchar()` перед возвратом из `main()`, гарантируя сохранение окна консоли открытым.

---

## Дорожная карта дальнейшего развития (Roadmap)

Каждый этап выполняется последовательно, тестируется отдельным примером, отмечается галочками `[x]` и фиксируется отдельным коммитом в Git.

### [x] Этап 9: Мульти-архетипный ECS рантайм (Dynamic Archetypes)
- [x] Поддержка сущностей с произвольными комбинациями компонентов (Archetype Record / Archetype Graph).
- [x] Динамическое добавление/удаление компонентов у сущностей на лету:
  - Функции `world_add_Position(entity, x, y)`, `world_remove_Position(entity)`.
  - Функции проверки наличия `world_has_Position(entity) -> bool`.
  - Миграция сущностей между архетипами (Archetype Transition) с сохранением существующих данных компонентов и swap-remove из старого архетипа.
- [x] Оптимизация запросов (`query`):
  - Поиск всех подходящих архетипов, содержащих запрашиваемый набор компонентов по битовой маске.
  - Итерация только по соответствующим чанкам памяти (быстрый перебор без фильтрации на каждой сущности внутри внутреннего цикла).
- [x] Сквозной тест: симуляция [`examples/multi_archetype.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/multi_archetype.ecs) с разнородными типами сущностей (`Position + Velocity + PlayerTag`, `Position + Obstacle`, `Position + Velocity`). Динамическое добавление и удаление скорости проверено нативно.
- [x] Полная обратная совместимость со всеми существующими тестами и бенчмарком 100k частиц.

### [x] Этап 10: Универсальные изолированные контексты миров (`World`) и синтаксис методов
- [x] Архитектурный переход от монолитного глобального ECS к универсальным изолированным структурам данных `World`.
- [x] Расширение синтаксиса языка:
  - Оператор доступа к модулям `::` (`TokenType.ColonColon`) для `ecs::create_world()`.
  - AST-узел `MethodCallExpression` и парсинг вызова методов: `world.spawn()`, `world.set_Position(...)`, `world.add_Velocity(...)`, `world.remove_Velocity(...)`, `world.has_Velocity(...)`, `world.sort_hierarchy()`.
- [x] Семантика и типизация:
  - Новый примитивный тип `TypeSymbol.World`.
  - Валидация методов над `World` и передача инстансов миров в конвейеры (`GamePipeline(game_world)`).
- [x] Машинный код LLVM:
  - Динамическое выделение структуры `%struct.EcsWorld` в `malloc`.
  - Передача указателя `worldPtr` (`ptr %world`) первым параметром во все ECS функции, системы (`@system_*`) и пайплайны (`@pipeline_*`).
  - Встраивание ресурсов (`struct.res.*`) внутрь структуры соответствующего мира с динамическим вычислением смещения, исключающее глобальные синглтоны.
- [x] Сквозной тест: [`examples/multi_world.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/multi_world.ecs) — одновременная изолированная работа игрового мира (`game_world`) и графического интерфейса (`ui_world`) в одном процессе.
- [x] Миграция всех существующих тестов и примеров на синтаксис методов мира.

### [x] Этап 11: Расширение синтаксиса и выразительности языка
- [x] Пользовательские функции:
  - Синтаксис `fn name(param: Type, ...): ReturnType { ... }`.
  - Pass 1 forward-декларации функций в LLVM IR, позволяющие вызывать функции в любом порядке из других функций и систем.
  - Поддержка параметров и возвращаемых значений примитивных типов и пользовательских структур.
- [x] Пользовательские структуры данных (`struct`):
  - Декларация: `struct Vector2 { x: f32, y: f32 }`.
  - Создание экземпляров через вызовы конструкторов: `Vector2(10.0, 20.0)`.
  - Чтение полей `pt.x` и модификация через операторы присваивания `pt.x += 100.0`.
  - Интеграция с системой типов и SoA/структурным эмиттером LLVM.
- [x] Диапазонные циклы `for`:
  - Синтаксис `for i in start..end { ... }`.
  - Семантическая валидация целочисленных границ и локальной области видимости переменной цикла.
  - Генерация базовых блоков `for_cond`, `for_body`, `for_inc` и `for_exit` в LLVM IR.
- [x] Массивы фиксированного размера `[T; N]`:
  - Синтаксис типов `[T; N]` в компонентах, структурах, ресурсах, событиях, функциях и локальных переменных.
  - Литералы массивов `[10, 20, 30]`.
  - Индексация чтения `arr[i]`, `pos.items[i]` и мутации `arr[i] = val;`, `arr[i] += val;`, `pos.items[i] += val;`.
  - Машинная генерация LLVM: плотные массивы `[N x T]`, вычисление адресов элементов через `InBoundsGEP2` без лишних копий.
  - Сквозной тест: [`examples/arrays_test.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/arrays_test.ecs).
- [x] Перечисления `enum` и сопоставление с образцом `match`:
  - Декларации перечислений: `enum State { Idle, Running, Attacking = 10, Dead }`.
  - Автоматическая и явная нумерация дискриминантов, 32-битное машинное представление.
  - Конструкция `match` с сопоставлением по членам enum (`State.Idle => { ... }`), целочисленным константам (`100 => { ... }`) и веткой по умолчанию (`_ => { ... }`).
  - Компиляция `match` в нативные таблицы переходов LLVM `switch` с $O(1)$ диспетчеризацией.
  - Сквозной тест: [`examples/enums_match_test.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/enums_match_test.ecs).

### [x] Этап 12: Графика, окно и ввод (Raylib / Window Integration)
- [x] Подключение библиотеки Raylib (C ABI, static `raylib.lib` под Windows x64) в `MsvcLinker` с динамическим UCRT (`msvcrt.lib`, `vcruntime.lib`, `ucrt.lib`) и Win32 подсистемами (`user32.lib`, `gdi32.lib`, `winmm.lib`, `shell32.lib`, `opengl32.lib`).
- [x] Встроенные функции управления окном: `init_window(w, h, title)`, `window_should_close()`, `close_window()`, `set_target_fps(60)`, `get_fps()`, `get_frame_time()`, `get_time()`.
- [x] Встроенные функции рендеринга 2D: `begin_drawing()`, `end_drawing()`, `clear_background(...)`, `draw_rectangle(...)`, `draw_circle(...)`, `draw_text(...)`, `draw_line(...)`, упаковка цветов RGBA `rl_color(r, g, b, a)`.
- [x] Автоматическое приведение координат и радиусов (`EnsureInt32`, `EnsureFloat`), позволяющее передавать `f32` поля компонентов напрямую в функции рендеринга без явных кастов.
- [x] Встроенные функции опроса ввода: `is_key_down(key)`, `is_key_pressed(key)`, `is_key_released(key)`, `is_key_up(key)`, `get_mouse_x()`, `get_mouse_y()`, `is_mouse_button_down(btn)`, `is_mouse_button_pressed(btn)`.
- [x] Интеграция рендеринга и ввода с ECS-пайплайнами: игровой цикл 60 FPS, обновление позиций через системы движения и отрисовка через системы рендера.
- [x] Сквозная интерактивная игра: [`examples/raylib_game.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/raylib_game.ecs) — 60 FPS окно, цветные прыгающие шары в ECS-архетипах и перемещаемая ракетка под управлением стрелочек или клавиш A/D. Скомпилировано в автономный `.exe` размером ~600 КБ.

### [x] Этап 13: События и реактивность (Events & Observers)
- [x] Декларация событий: `event OnClick { target: i32, mouse_x: f32, mouse_y: f32 }`, `event Collision { entity_a: i32, entity_b: i32, force: f32 }`.
- [x] Генерация очередей событий в структуре мира (двойной буфер очередей Event Buffer: `read_data` и `write_data` с $O(1)$ zero-copy swap указателей).
- [x] Отправка событий из систем или пользовательского кода: `world.emit_Event(...)` и конструкторный синтаксис `world.emit(Event(...))`.
- [x] Подписка систем на события: `system DamageObserver { read(col: Collision, mut stats: GameStats) { ... } }`.
- [x] Смена буферов событий: автоматическая при старте конвейера и явная через `swap_events;` на границе стадий пайплайна или `world.swap_events()`.
- [x] Сквозной тест: [`examples/events_observers.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/events_observers.ecs) — комплексная проверка генерации событий из систем движения при достижении координат, реакции наблюдателей, модификации глобальных ресурсов и передачи событий между стадиями конвейера.

### [x] Этап 14: Многопоточность и Job System (Multithreaded Systems)
- [x] Анализ графа зависимостей систем на этапе семантики (DAG):
  - Построение множеств доступа `MutComponents`, `ConstComponents`, `MutResources`, `ConstResources`.
  - Статическая проверка отсутствия гонок данных (Data-Race Free) в параллельных блоках `parallel { ... }` с точной диагностикой конфликтов (`write-read`, `write-write`, `read-write`).
  - Системы, читающие одни и те же компоненты (`const`), выполняются параллельно без блокировок.
- [x] Легковесный нативный ThreadPool (Win32 Thread Pool API: `CreateThreadpoolWork`, `SubmitThreadpoolWork`, `WaitForThreadpoolWorkCallbacks`, `CloseThreadpoolWork` из `kernel32.dll`).
- [x] Генерация функций обратного вызова `job_{SystemName}` (`PTP_WORK_CALLBACK`) и параллельный диспетчер стадий конвейера.
- [x] Барьеры синхронизации `sync;` и встроенные функции замера времени `get_tick_count()` / `time_ms()`.
- [x] Сквозной бенчмарк: [`examples/multithreading_benchmark.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/multithreading_benchmark.ecs) — параллельная обработка 100 000 сущностей с 4 компонентами на всех ядрах процессора и сравнение с последовательным выполнением.

### [x] Этап 15: Отложенные команды мутации мира (Command Buffer / `commands`)
- [x] Синтаксис и система типов:
  - Тип `Commands` (или параметр `cmd: Commands`), доступный в запросах систем: `query(mut pos: Position, cmd: Commands)` или `query(mut a: Actor, cmd: Commands)`.
  - Методы буфера команд: `cmd.spawn()`, `cmd.despawn(entity)`, `cmd.add_Comp(entity, ...)`, `cmd.remove_Comp(entity)`.
  - Действие пайплайна `apply_commands;` в стадиях конвейера и метод `world.apply_commands()`.
- [x] Архитектура буфера команд в рантайме LLVM:
  - Структура очереди команд `CommandBuffer` в памяти мира: тип команды (Spawn, Despawn, Add, Remove), ID сущности, ID компонента, байтовый буфер данных.
  - Функция `world_apply_commands(world)`: пакетное наложение накопленных команд с миграцией архетипов вне циклов систем.
- [x] Безопасность и многопоточность:
  - Возможность безопасного спавна и удаления сущностей прямо во время параллельного выполнения систем в `parallel { ... }` через потокобезопасный Win32 `SRWLOCK`.
- [x] Сквозные тесты:
  - [`examples/command_buffer_test.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/command_buffer_test.ecs) — динамический спавн пуль и удаление уничтоженных сущностей прямо из систем движения без гонок и сбоев итерации.
  - [`examples/parallel_commands_test.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/parallel_commands_test.ecs) — параллельная запись команд из нескольких параллельных систем в пуле потоков без коллизий.

### [x] Этап 16: Оптимизации LLVM и релизные сборки (`--release`, `-O3`, отладка `-g`)
- [x] Интеграция LLVM Pass Manager (PassBuilder / New Pass Manager) в `LlvmCodeGenerator`:
  - Уровни оптимизации `-O0`, `-O1`, `-O2`, `-O3`, `-Os`, `-Oz`.
  - Включение проходов векторизации (Loop Vectorize, SLP Vectorize), инлайнинга функций, Loop Unroll, InstCombine, Dead Code Elimination.
  - Флаг CLI `--release` (компиляция с `-O3`, линковка с `/OPT:REF /OPT:ICF` и отключение паузы Enter).
  - Флаг CLI `--no-wait` (отключение паузы «Press Enter» для CLI и скриптов).
- [x] Генерация отладочной информации (DIBuilder):
  - Флаги `--debug` / `-g`: генерация метаданных CodeView (PDB под Windows) и DWARF.
  - Привязка исходных строк и колонок (`SourceSpan`) к инструкциям LLVM для пошаговой отладки в Visual Studio / VS Code / LLDB.
- [x] Бенчмарк: сравнение времени симуляции 100k частиц в Debug и Release (`-O3`):
  - Сквозной тест: [`examples/optimization_benchmark.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/optimization_benchmark.ecs) — 140 ms (Debug -O0) против 47 ms (Release -O3) на 20 кадрах (**ускорение 3.0x**).

### [x] Этап 17: Модульность и многофайловые проекты (`import` / модули)
- [x] Синтаксис директивы импорта:
  - Строковые пути: `import "path/to/file.ecs";`
  - Идентификаторы модулей: `import components;` (автоматически дополняется до `components.ecs`).
- [x] Загрузчик проектов `ProjectLoader`:
  - Рекурсивный обход дерева импортов с нормализацией путей через `Path.GetFullPath`.
  - Построение графа зависимостей и объединение AST в топологическом post-order порядке (зависимости гарантированно объявляются до использующих их файлов).
  - Алгоритм обнаружения циклических зависимостей (`_inProgressStack`) с понятной диагностикой цикла (например, `cycle_a.ecs -> cycle_b.ecs -> cycle_a.ecs`).
  - Устранение дубликатов (Diamond Dependency resolution): при импорте одного модуля несколькими файлами декларации попадают в программу строго один раз.
- [x] Полная интеграция с `ECSLang.CLI`: команды `ecs build <entry.ecs>` и `ecs run <entry.ecs>` автоматически резолвят все зависимые модули.
- [x] Сквозной демонстрационный проект: [`examples/multi_file/`](file:///C:/Users/office/Documents/ECS_Lang/examples/multi_file/)
  - `components.ecs` — объявления компонентов `Position`, `Velocity`, `Health`.
  - `systems.ecs` — системы `MovementSystem`, `HealthRegenSystem`.
  - `pipeline.ecs` — оркестрация конвейера `GamePipeline`.
  - `main.ecs` — точка входа `fn main(): i32` с симуляцией 5 тиков конвейера.
- [x] Сквозной тест цикла зависимостей: проверено корректное прерывание компиляции с диагностикой `Circular dependency detected`.

### [x] Этап 18: Комплексный игровой демо-проект (2D Arcade Shooter)
- [x] Полноценная игра "Void Defender" на Raylib, объединяющая все подсистемы языка:
  - Игровой цикл 60 FPS с нативным рендерингом Raylib (`init_window`, `begin_drawing`, `end_drawing`, `clear_background`, `draw_rectangle`, `draw_circle`, `draw_text`, `draw_line`).
  - Управление космическим кораблем игрока (стрелочки / WASD / пробел) с ограничением по экрану.
  - Вражеские корабли с конечным автоматом состояний на перечислениях и сопоставлении шаблонов: `enum EnemyState { Patrolling, Diving, Evading }` и `match en.state`.
  - Отложенные команды мутации мира (`Commands`): спавн и деспавн лазеров, врагов и партиклов взрыва через `cmd.spawn()` и `cmd.despawn()`.
  - События и наблюдатели: `event HitEvent { x, y, points }`, генерация из систем через `world.emit_HitEvent(...)` и реакция в `system HitObserver { read(hit: HitEvent, mut state: GameState, cmd: Commands) }`.
  - Параллельное обновление звездного фона в пуле потоков через `stage Background { parallel { StarfieldSystem; } }`.
  - Многофайловая модульная архитектура: [`examples/arcade_shooter/`](file:///C:/Users/office/Documents/ECS_Lang/examples/arcade_shooter/)
    - `types.ecs` — объявления перечислений `EnemyState`, компонентов, ресурсов и событий.
    - `systems.ecs` — системы физики, искусственного интеллекта врагов, ввода и наблюдатели событий.
    - `render.ecs` — системы отрисовки космического пространства, кораблей, лазеров, взрывов и HUD.
    - `pipeline.ecs` — конвейер `ArcadePipeline` с параллельными стадиями, `swap_events;` и `apply_commands;`.
    - `main.ecs` — точка входа с инициализацией мира, ресурсов и 60 FPS игровым циклом.
- [x] Сборка в автономный оптимизированный нативный бинарник Windows x64 `.exe` (`--release`, `-O3`).

### [ ] Этап 19: Расширенная работа со строками и форматирование
- [ ] Конкатенация строк (`+`) и преобразование чисел в строки (`to_string`).
- [ ] Интерполяция строк в синтаксисе языка.
- [ ] Базовая стандартная библиотека математических и строковых утилит.


