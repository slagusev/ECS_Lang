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

### [x] Этап 19: Расширенная работа со строками и форматирование
- [x] Конкатенация строк (`+`):
  - Конкатенация двух строк: `str1 + str2`.
  - Автоматическое приведение при конкатенации строки и числа/булева значения: `str + num`, `num + str`, `str + bool`.
- [x] Преобразование значений в строки (`to_string`):
  - Встроенная функция `to_string(x)` и метод `x.to_string()` для типов `i32`, `f32`, `bool`, `entity`.
  - Высокопроизводительные нативные C-runtime хелперы: `rt_to_string_i32`, `rt_to_string_f32`, `rt_to_string_bool`.
- [x] Определение длины строки и свойства:
  - Доступ к свойству `.len` и `.length` (`greeting.len`).
  - Вызов метода `.len()` и `.length()` (`greeting.len()`).
  - Встроенная функция `str_len(s)`.
- [x] Сравнение строк:
  - Операторы `==` и `!=` для строк с вызовом CRT `strcmp` (`rt_str_eq`).
- [x] Интерполяция строк:
  - Поддержка синтаксиса в стиле C# (`$"Player: {name}, Score: {score}"`) и Python (`f"Level: {level + 1}"`).
  - Разворачивание интерполяции на уровне синтаксического анализатора (AST Desugaring) в конкатенацию подвыражений с автоматическим оборачиванием в `to_string(...)`.
- [x] Встроенная стандартная математическая библиотека:
  - Функции с плавающей точкой: `sqrt(x)`, `sin(x)`, `cos(x)`, `floor(x)`, `ceil(x)`.
  - Универсальные функции (float и int): `abs(x)`, `min(a, b)`, `max(a, b)`, `clamp(val, min, max)`.
  - Генерация случайных чисел: `rand()` и `rand_range(min, max)`.
- [x] Сквозной тест: [`examples/strings_math_test.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/strings_math_test.ecs), успешно пройден как в Debug (`-O0`), так и в Release (`-O3`).

### [x] Этап 20: Продвинутые фильтры запросов ECS (`without` и `with`)
- [x] Синтаксис фильтров в запросах систем:
  - Исключение компонентов: `system S { query(mut pos: Position, vel: Velocity, without: Frozen, without: Dead) { ... } }`
  - Маркерные компоненты (без загрузки данных в стек): `system S { query(e: Entity, with: Player, mut health: Health) { ... } }`
- [x] Расширение AST и фронтенда:
  - Ключевые слова `without` и `with` в `Lexer` и `Token`.
  - Поле `Filters` (`IReadOnlyList<QueryFilter>`) в узле `SystemDeclaration`.
  - Парсинг фильтров в сигнатуре `query(...)` в `Parser.cs`.
- [x] Семантический анализ (`TypeChecker.cs`):
  - Проверка, что типы в `with` и `without` являются объявленными компонентами (`component`).
  - Проверка на конфликты: компонент не может одновременно присутствовать в `query` / `with` и в `without`.
- [x] Генерация машинного кода в LLVM IR (`LlvmCodeGenerator.cs`):
  - Фильтрация архетипов мира перед входом в цикл системы: отбираются только архетипы `(archMask & requiredMask == requiredMask) && (archMask & withoutMask == 0)`.
  - Отсутствие runtime-оверхеда во внутреннем цикле (zero-overhead loop).
- [x] Сквозной тест: [`examples/query_filters_test.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/query_filters_test.ecs), успешно пройден как в Debug (`-O0`), так и в Release (`-O3`).

### [x] Этап 21: Текстуры, Спрайты, Аудио и 2D Камера (Raylib Media)
- [x] Поддержка дескрипторов Raylib ресурсов: `Texture2D`, `Sound`, `Camera2D`, `Rectangle`, `Vector2`.
- [x] Функции загрузки, геометрии и рендеринга текстур: `load_texture`, `unload_texture`, `get_texture_width`, `get_texture_height`, `draw_texture`, `draw_texture_pro` (с поддержкой вращения, масштабирования и tint).
- [x] Подсистема аудио: `init_audio_device`, `close_audio_device`, `is_audio_device_ready`, `load_sound`, `unload_sound`, `play_sound`, `stop_sound`, `pause_sound`, `resume_sound`, `is_sound_playing`, `set_sound_volume`.
- [x] Подсистема 2D камеры: `begin_mode_2d(ox, oy, tx, ty, rot, zoom)`, `end_mode_2d()`.
- [x] Маршалинг структур по соглашению Microsoft x64 C ABI (sret для возврата `LoadTexture`/`LoadSound`, передача структур по ссылке/значению).
- [x] Сквозной тест: [`examples/raylib_media_test.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/raylib_media_test.ecs), успешно протестирован в Debug (`-O0`) и Release (`-O3`).

### [x] Этап 22: Методы пользовательских структур (`impl Struct`)
- [x] Синтаксис блоков реализации `impl StructName { fn method(self, ...): Ret { ... } }`.
- [x] Передача экземпляра структуры через параметр `self` и мутирующий `mut self` (указатель `this` / zero-copy in-place).
- [x] Статические/ассоциированные методы `StructName::method(...)`.
- [x] Семантическая проверка и кодогенерация вызовов методов через точечную нотацию `vec.length_sq()`, `vec.scale(2.0)`.
- [x] Поддержка методов на компонентах ECS с прямой мутацией в чанках архетипов: `query(mut pos: Position) { pos.translate(...); }`.
- [x] Сквозной тест: [`examples/impl_methods_test.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/impl_methods_test.ecs), успешно пройден в Debug (`-O0`) и Release (`-O3`).

### [x] Этап 23: Динамические массивы (`Vec<T>` / `List<T>` / `[T]`)
- [x] Встроенная обобщенная коллекция переменной длины на базе динамического буфера памяти (структура `{ T* data, i32 length, i32 capacity }` — 16 байт на x64).
- [x] Синтаксис типов: `Vec<T>`, `List<T>`, срез `[T]`, обобщенные конструкторы `Vec<T>()`, `List<T>()` и инициализатор `[]`.
- [x] Методы `.push(item)` (с автоматическим экспоненциальным ростом через CRT `realloc`), `.pop()`, `.len()` / `.length()`, `.capacity()`, `.clear()` и индексация `arr[i]`, `arr[i] = val`.
- [x] Итерация по динамическим массивам через прямой синтаксис `for item in arr { ... }` с десугаризацией в индексированный цикл в AST.
- [x] Сквозной тест: [`examples/dynamic_arrays_test.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/dynamic_arrays_test.ecs), успешно пройден как в Debug (`-O0`), так и в Release (`-O3`).

### [x] Этап 24: Встроенный визуальный ECS-профайлер и инспектор (F1)
- [x] Оверлей в Raylib по нажатию функциональной клавиши F1 (`IsKeyPressed(290)`): переключаемый HUD с темной полупрозрачной подложкой и неоновой рамкой.
- [x] Zero-overhead Headless Guard: автоматическая проверка `IsWindowReady()` при вызове `world.render_profiler()` исключает краши в консольных и тестовых приложениях.
- [x] Метрики производительности кадра: вывод FPS (`GetFPS()`) и Frame Time в миллисекундах (`GetFrameTime() * 1000.0`).
- [x] Инспектор состояния мира: суммарное число активных сущностей (`live entities`), число зарегистрированных архетипов.
- [x] Инспектор архетипов: обход `arch_array` с отображением шестнадцатеричной маски компонентов, количества сущностей в чанках и их емкости.
- [x] Интеграция API: поддержка методов `world.render_profiler()`, `world.render_debug_overlay()` и функции `render_profiler(world)`.
- [x] Интеграция в интерактивную игру [`examples/raylib_game.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/raylib_game.ecs).
- [x] Сквозной тест профайлера: [`examples/profiler_test.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/profiler_test.ecs), проверен в Debug (`-O0`) и Release (`-O3`).

### [x] Этап 25: Хэш-таблицы (`HashMap<K, V>`) и именованная индексация
- [x] Встроенная обобщенная хэш-таблица `HashMap<K, V>` / `Map<K, V>` (открытая адресация, линейное пробирование, FNV-1a для строк, мультипликативный хэш Кнута для целых чисел и сущностей, bitcast для вещественных).
- [x] Индексация сущностей по строковым именам в структуре `World`: `world.set_name(entity, name)`, `world.get_by_name(name) -> entity`, `world.has_name(name) -> bool`.
- [x] Методы `.insert(k, v)`, `.get(k)`, индексирование `map[k]` и `map[k] = v`, `.contains(k)`, `.remove(k)`, `.len()`, `.capacity()`, `.clear()`.
- [x] Автоматический рехэшинг при коэффициенте заполнения $> 70\%$ с выделением степени двойки.
- [x] Сквозной тест ассоциативных коллекций и именованной индексации: [`examples/hashmap_test.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/hashmap_test.ecs), успешно пройден как в Debug (`-O0`), так и в Release (`-O3`).

### [x] Этап 26: Аренное управление памятью строк (`World` String Arena)
- [x] Встроенный аренный аллокатор строк в структуре контекста `World` (монолитные чанки памяти по 64 КБ со связанным списком `%struct.StringArenaChunk`).
- [x] Быстрое выделение временных строк при конкатенации (`+`), форматировании (`to_string`) и интерполяции строк (`$"..."`) с bump-pointer без фрагментации системной кучи CRT.
- [x] Мгновенный $O(k)$ сброс строковой арены кадра / мира через `world.reset_string_arena()` без системных вызовов `free`.
- [x] Методы мира: `world.reset_string_arena() -> void` и `world.alloc_string(capacity: i32) -> string`.
- [x] Полное освобождение памяти арены при деструкции мира `world.free()` / `world.destroy()`.
- [x] Сквозной стресс-тест строковой арены: [`examples/string_arena_test.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/string_arena_test.ecs) (выделение 10 000 строк в кадровом цикле с периодическим сбросом арены), успешно пройден в Debug (`-O0`) и Release (`-O3`).

### [x] Этап 27: Безопасные типы `Option<T>` и `Result<T, E>` с Pattern Matching
- [x] Алгебраические типы данных (Tagged / Discriminated Unions) в системе типов: `%struct.Option_T = { i32, T }` и `%struct.Result_T_E = { i32, T, E }`.
- [x] Тип `Option<T>`: варианты `Some(val)` и `None`, вспомогательные методы `.is_some()`, `.is_none()`, `.unwrap()`, `.unwrap_or(default)`.
- [x] Тип `Result<T, E>`: варианты `Ok(val)` и `Err(err)`, вспомогательные методы `.is_ok()`, `.is_err()`, `.unwrap()`, `.unwrap_err()`, `.unwrap_or(default)`.
- [x] Интеграция безопасных методов: `world.find(name) -> Option<Entity>`, `world.find_entity(name) -> Option<Entity>`, `arr.get(idx) -> Option<T>`, `map.find(key) -> Option<V>`.
- [x] Интеграция с конструкцией `match` для безопасного разворачивания с привязкой локальных переменных (`Some(x) => ...`, `None => ...`, `Ok(v) => ...`, `Err(e) => ...`).
- [x] Сквозной тест безопасности и сопоставления: [`examples/option_result_test.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/option_result_test.ecs), успешно пройден в Debug (`-O0`) и Release (`-O3`).

### [x] Этап 28: Стандартная библиотека ECS GUI (`Button`, `Text`, `Slider`, `Panel`) на чистом ECSLang
- [x] Стандартная библиотека написана **целиком на чистом ECSLang** (файлы `std/gui.ecs`, `std/gui/components.ecs`, `std/gui/events.ecs`, `std/gui/systems.ecs`, `std/gui/widgets.ecs`, `std/gui/pipeline.ecs`), без захардкоженных типов в C# компиляторе.
- [x] Разрешение путей стандартных модулей: в `ProjectLoader.cs` добавлена поддержка резолвинга импортов `std/...` относительно корня компилятора и корня проекта.
- [x] Расширенный набор из 22 ECS GUI компонентов: `UIRect`, `UIPos`, `UISize`, `UIPadding`, `UIMargin`, `UIAnchor`, `UILayout`, `UIZOrder`, `UIBackground`, `UIShadow`, `UIText`, `UITexture`, `UITooltip`, `UIState`, `UIButton`, `UICheckbox`, `UISlider`, `UIProgressBar`, `UIPanel`, `UIRadioButton`, `UIToggleSwitch`, `UIBadge`, `UISeparator`, `UIInputField`, `UIScrollArea`.
- [x] Реактивные ECS события: `event UIEventClick`, `event UIEventValueChanged`, `event UIEventToggle`, `event UIEventDrag`.
- [x] Системы взаимодействия и рендеринга на ECSLang: `UIInputSystem`, `UIButtonClickSystem`, `UICheckboxToggleSystem`, `UISliderDragSystem`, `UIRenderPanels`, `UIRenderButtons`, `UIRenderLabels`, `UIRenderCheckboxes`, `UIRenderSliders`, `UIRenderProgressBars`, `UIRenderSeparators`, `UIRenderTooltips` с поддержкой `without: Component`.
- [x] Конструкторы виджетов: `gui_panel`, `gui_button`, `gui_label`, `gui_checkbox`, `gui_slider`, `gui_progress_bar`, `gui_toggle_switch`, `gui_radio_button`, `gui_separator`, `gui_tooltip`.
- [x] Конвейер GUI: `pipeline GUIPipeline` со стадиями `Update` и `Render`.
- [x] Защита от сбоев в headless-режиме: `EmitGuardedRaylibVoidCall` с проверкой `IsWindowReady()` исключает краши OpenGL при выполнении тестов или консольных приложений.
- [x] Безопасность C ABI для булевых функций Raylib: объявление функций с возвратом `Int8Type` и сравнение `ne 0` исключает попадание мусора в верхние биты регистра.
- [x] Сквозной тест стандартной библиотеки: [`examples/gui_standard_lib_test.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/gui_standard_lib_test.ecs) (22 компонента, все виджеты, 10 фреймов `GUIPipeline`), успешно пройден в Debug (`-O0`) и Release (`-O3`).

### [x] Этап 29: Замыкания и лямбды (`|e| ...`)
- [x] Компактный синтаксис анонимных функций: `|x: i32| x * 2`, `|a: i32, b: i32| -> i32 { return a + b; }`, `|| 42`.
- [x] Функциональные типы данных: `fn(T1, T2): Ret` и `fn(T1) -> Ret` в системе типов и AST (`TypeSymbol.IsFunction`, `TryGetFunctionInfo`).
- [x] Семантический анализ и захват окружения (`TypeChecker.FindCaptures`): автоматическое обнаружение используемых внешних локальных переменных.
- [x] Унифицированное представление Fat Pointer: `%Closure = type { ptr, ptr }` (`{ fn_ptr, env_ptr }`).
- [x] Стековый захват переменных с нулевыми аллокациями в куче (Zero Heap Allocation): фрейм окружения `%struct.ClosureEnv_N` создается на стеке вызывающего контекста, гарантируя отсутствие GC и фрагментации памяти.
- [x] Чтение и мутация захваченных переменных: адрес переменной передается во фрейм окружения, позволяя прямой доступ на запись (`total += step`).
- [x] Кодогенерация функций `@__lambda_N(ptr %env, args...)` и косвенных вызовов `IndirectCallExpression` и вызовов переменных-замыканий.
- [x] Встроенные методы высшего порядка над динамическими массивами (`Vec<T>` / `[T]`):
  - `.for_each(closure)`: итерация по элементам с вызовом замыкания.
  - `.map(closure)`: преобразование элементов с созданием нового массива `Vec<R>`.
  - `.filter(predicate)`: фильтрация элементов по предикату в новый массив `Vec<T>`.
  - `.any(predicate)` / `.all(predicate)`: квантификаторы с ранним выходом.
  - `.find(predicate)`: поиск элемента с возвратом `Option<T>` (`Some(val)` / `None`).
- [x] Сквозной тест замыканий и лямбда-выражений: [`examples/closures_test.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/closures_test.ecs), успешно пройден как в Debug (`-O0`), так и в Release (`-O3`).

### [x] Рефакторинг компилятора: Модульное разделение сверхкрупных файлов (перед Этапом 30)
- [x] Разделение `LlvmCodeGenerator.cs` на логические модули через `partial class`:
  - `LlvmCodeGenerator.cs` (базовый конвейер, debug info, аллокации, строки, функции).
  - `LlvmCodeGenerator.Expressions.cs` (арифметика, вызовы функций, методы, замыкания).
  - `LlvmCodeGenerator.Statements.cs` (ветвления, циклы, сопоставление match, присваивания).
  - `LlvmCodeGenerator.Ecs.cs` (системы, пайплайны, фазы).
  - `LlvmCodeGenerator.Raylib.cs` (привязки Raylib C ABI, цвета).
- [x] Разделение `EcsRuntimeEmitter.cs` на модули:
  - `EcsRuntimeEmitter.cs` (метаданные компонентов, MapType, структура struct.EcsWorld).
  - `EcsRuntimeEmitter.Archetypes.cs` (SoA память, чанки, миграции архетипов, буфер команд, события).
  - `EcsRuntimeEmitter.Profiler.cs` (F1 HUD оверлей, статистика мира).
- [x] Разделение `TypeChecker.cs` на модули:
  - `TypeChecker.cs` (символы, области видимости, таблицы, валидация программы).
  - `TypeChecker.Expressions.cs` (проверка выражений, замыканий, методов).
  - `TypeChecker.Statements.cs` (проверка инструкций и блоков).
  - `TypeChecker.Declarations.cs` (проверка объявлений систем, пайплайнов, функций).
- [x] Разделение `Parser.cs` на модули:
  - `Parser.cs` (ядро парсера, токенизация, навигация).
  - `Parser.Declarations.cs` (декларации функций, компонентов, систем, структур и т.д.).
  - `Parser.Statements.cs` (инструкции, присваивания, блоки).
  - `Parser.Expressions.cs` (выражения с приоритетами, лямбды, вызовы).
- [x] Регрессионная валидация: полное прохождение тестов (closures, dynamic arrays, hashmap, raylib_game) и фиксация в git (коммит `d85a2bf`).

### [x] Этап 30: Обобщенные компоненты и системы (Generics + Traits)
- [x] Обобщенные параметры типов `<T>`, `<T, U>` для:
  - Структур: `struct Pair<T, U> { first: T, second: U }`.
  - Блоков реализации методов: `impl<T, U> Pair<T, U> { fn get_first(self): T { return self.first; } }`.
  - Функций: `fn identity<T>(x: T): T { return x; }`.
  - Компонентов ECS: `component Node<T> { value: T }`.
  - Систем ECS: `system NodePrinter<T> { query(n: Node<T>) { println(n.value); } }`.
  - Действий конвейеров: `pipeline TestPipeline { stage S { NodePrinter<i32>; NodePrinter<string>; } }`.
- [x] Интерфейсы / Трейты (`trait`) и ограничения параметров типов (`<T: Trait>`):
  - Декларации трейтов: `trait Printable { fn print(self); }`.
  - Реализация трейта для типа: `impl Printable for Point { fn print(self) { ... } }`.
  - Ограничение параметров типов в обобщенных функциях: `fn show<T: Printable>(item: T) { item.print(); }`.
  - Статическая проверка соответствия сигнатур методов трейтов при компиляции.
- [x] Статическая компиляторная мономорфизация (Zero-Cost Monomorphization):
  - Выделенный семантический анализатор `TypeChecker.Monomorphization.cs`.
  - Полная подстановка типов и выражений с генерацией конкретных мономорфных типов (`Node_i32_`, `Pair_i32_string_`).
  - Разрешение коллизий и канонический порядок регистрации компонентов `_orderedCompNames` в `EcsRuntimeEmitter`, исключающий дублирование и рассинхронизацию битовых масок и столбцов SoA-архетипов.
- [x] Надежное удержание окна консоли на Windows:
  - Использование прямого консольного ввода `_getch()` вместо потокового `getchar()`, предотвращающее мгновенное закрытие консольного окна из-за остаточных переводов строк `\n` в `stdin`.
  - Поддержка явного вызова `wait_key()`, а также автоматической паузы перед выходом из `main` при отсутствии интерактивных Raylib-окон.
- [x] Сквозной тест обобщений и трейтов: [`examples/generics_test.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/generics_test.ecs), успешно пройден с кодом возврата 0.

### [x] Этап 31: Кроссплатформенность компилятора и тулчейна (Linux, macOS, Windows)
- [x] Модель целевых платформ в `ECSLang.Core/TargetPlatform.cs`:
  - Перечисления `PlatformKind` (`Windows`, `Linux`, `MacOS`) и `CpuArchitecture` (`X64`, `Arm64`, `X86`, `Arm`).
  - Класс `TargetProfile` с тройками LLVM (Target Triples), расширениями файлов исполняемых модулей (`.exe` vs пустое) и объектных файлов (`.obj` vs `.o`), предопределенными профилями (`WindowsX64`, `WindowsArm64`, `LinuxX64`, `LinuxArm64`, `MacosArm64`, `MacosX64`) и автоопределением хост-платформы `TargetProfile.HostDefault`.
  - Парсинг троек и алиасов `--target <triple>` и `--os <platform>` (`win`, `linux`, `macos`).
- [x] Архитектура компоновщиков в `ECSLang.Toolchain`:
  - Интерфейс `ILinker` с методом `Link(objFilePath, outputExePath, options)`.
  - Реализация `MsvcLinker`: поиск MSVC `link.exe`, Windows SDK через `vswhere.exe`, компоновка PE/COFF `.exe` под Windows.
  - Реализация `ClangGccLinker`: поддержка `clang`, `gcc`, `lld` с автоматической передачей флагов POSIX (`-lm`, `-lpthread`, `-ldl`, `-lrt`), оптимизаций компоновщика (`--gc-sections` на Linux, `-dead_strip` на macOS) и фреймворков macOS (`-framework OpenGL -framework Cocoa -framework IOKit -framework CoreVideo`).
  - Фабрика `LinkerFactory`: динамическое создание подходящего линковщика в зависимости от целевой платформы и хост-окружения.
  - Грациозный кросс-компиляционный фоллбэк: сохранение объектных файлов `.o` при отсутствии установленного кросс-линковщика с выводом готовой команды сборки для целевой ОС.
- [x] Кроссплатформенная генерация LLVM IR:
  - Корректная установка целевой тройки `target triple = "..."` и DataLayout для ELF (Linux), Mach-O (macOS) и COFF (Windows).
  - Генерация отладочной информации по целевым стандартам: DWARF (`Dwarf Version`, `Debug Info Version`) для POSIX vs CodeView (`CodeView`, `Debug Info Version`) для Windows MSVC.
  - Кроссплатформенная синхронизация архетипов: Win32 SRWLock (`AcquireSRWLockExclusive`/`ReleaseSRWLockExclusive`) на Windows и переносимый безигольчатый спинлок на атомиках LLVM (`cmpxchg` acquire-monotonic / `atomicrmw sub` release) для систем без Win32 API.
  - Платформо-зависимый консольный ввод: прямой ввод `_getch` из `msvcrt` на Windows и стандартный `getchar` на POSIX.
- [x] CLI-флаги компилятора:
  - `--target, -t <triple>`: компиляция под любую целевую платформу LLVM.
  - `--os <platform>`: быстрый выбор целевой ОС (`win`, `linux`, `macos`).
  - `-c, --emit-obj`: генерация целевого объектного файла без линковки.
- [x] Верификация кросс-компиляции:
  - Успешный запуск тестов на Windows (`generics_test.ecs`, `closures_test.ecs` с кодом 0).
  - Успешная сборка LLVM IR и объектных файлов для Linux ELF (`x86_64-unknown-linux-gnu`) и macOS Mach-O ARM64 (`arm64-apple-darwin`).

### [x] Этап 32: Сетевые компоненты и протоколы (TCP/UDP через ECS)
- [x] Низкоуровневый C ABI сетевой рантайм (`LlvmCodeGenerator.Net.cs`):
  - Кроссплатформенная поддержка сокетов: WinSock2 (`ws2_32.lib`, `WSAStartup`/`WSACleanup`, `ioctlsocket`, `closesocket`) под Windows и стандартный POSIX сокетный стек (`close`, `fcntl`/`ioctl`) под Linux/macOS.
  - Нативные примитивы: `net_start`, `net_stop`, `net_tcp_listen`, `net_tcp_connect`, `net_tcp_accept`, `net_tcp_send`, `net_tcp_recv`, `net_udp_bind`, `net_udp_send_to`, `net_udp_recv_from`, `net_close`, `net_get_last_error`.
  - Неблокирующий режим сокетов (`FIONBIO`), защита от коллизий портов и угона адресов (`SO_EXCLUSIVEADDRUSE` / проверка ошибок `bind`/`listen`).
- [x] Стандартная библиотека `std/net.ecs` в парадигме Pure ECS (Data-Oriented Design):
  - Компоненты (`std/net/components.ecs`): `TcpListener { port, fd, is_active }`, `TcpConnection { fd, peer_ip, peer_port, is_connected }`, `UdpSocket { port, fd }`, `NetAddress { ip, port }`, `PingStats { rtt_ms, packets_sent, packets_recv }`.
  - Реактивные события (`std/net/events.ecs`): `NetEventConnected { client_fd, ip, port }`, `NetEventDisconnected { fd, reason }`, `NetPacketReceived { fd, payload }`, `NetPacketSend { fd, payload }`.
  - Сетевые системы (`std/net/systems.ecs`):
    - `TcpListenerSystem`: опрос неблокирующих слушающих сокетов, спавн сущностей `TcpConnection` и отправка событий `NetEventConnected`.
    - `TcpReceiverSystem`: опрос TCP соединений в неблокирующем режиме, отправка событий `NetPacketReceived`.
    - `UdpReceiverSystem`: опрос неблокирующих UDP датаграмм в цикле `query(mut u: UdpSocket)`, отправка событий `NetPacketReceived`.
    - `OutboundSendSystem`: наблюдатель `read(packet: NetPacketSend)`, отправка исходящих сообщений в сокеты.
  - Конвейер (`std/net/pipeline.ecs`): `NetPipeline` со стадиями `NetPoll` (TCP listener, TCP receiver, UDP receiver) и `NetFlush` (`swap_events`, `OutboundSendSystem`).
- [x] Поддержка метода длины строк `.len()` наряду со свойством `.len` в `TypeChecker.Expressions.cs` и `LlvmCodeGenerator.Expressions.cs`.
- [x] Сквозной тест сетевой подсистемы: [`examples/net_echo_test.ecs`](file:///C:/Users/office/Documents/ECS_Lang/examples/net_echo_test.ecs) — одновременная неблокирующая передача данных по TCP и UDP через шину событий ECS, реакция систем `read(packet: NetPacketReceived)` и успешное завершение с кодом 0.

### [x] Этап 33: Фаза P0 — Системное ядро и управляющие конструкции (Стоп-кровотечение)
- [x] Пункт 2.2: Compile-Time константы (`const`):
  - Ключевое слово `const`, токен `TokenType.Const`, AST-узел `ConstDeclaration`.
  - Модульный парсер: `ParseConstDeclaration()` в `Parser.Declarations.cs`.
  - Pass 0.5 семантического анализа: `RegisterConstants()` в `TypeChecker.Declarations.cs`, предварительное вычисление константных выражений (Constant Folding) до проверки тел систем и функций.
  - Символ `ConstSymbol` в таблице символов, валидация невозможности мутации (`CannotAssignToConst`).
  - Zero-Cost LLVM кодогенерация: инлайнинг немедленных операндов (`LLVM.ConstInt`, `LLVM.ConstReal`, константный пул строк) в `LlvmCodeGenerator.Expressions.cs`.
  - Поддержка констант в ветвях сопоставления шаблонов `match`.
  - Модули стандартной библиотеки: `std/keys.ecs` (коды клавиш Raylib) и `std/mouse.ecs` (кнопки мыши и стили курсоров).
  - Сквозной тест: `examples/const_test.ecs` (код 0).
- [x] Пункт 2.3: Управление циклами (`break` и `continue`):
  - Ключевые слова `break` и `continue`, токены `TokenType.Break`/`TokenType.Continue`, AST-узлы `BreakStatementNode`/`ContinueStatementNode`.
  - Парсинг инструкций в `Parser.Statements.cs`.
  - Семантическая проверка контекста в `TypeChecker.Statements.cs`: счетчик вложенности `_loopDepth`, валидация ошибок `CannotBreakOutsideLoop` и `CannotContinueOutsideLoop`.
  - LLVM кодогенерация CFG: стек `_loopStack` с кортежами `(condBB, exitBB, forIncBB)`.
  - Семантика `break`: переход `BuildBr(exitBB)` на блок завершения цикла.
  - Семантика `continue`: в циклах `while` — переход на `condBB`; в диапазонах `for i in start..end` — корректный переход на блок инкремента `forIncBB` с шагом `i++` (предотвращение бесконечного зацикливания).
  - Сквозной тест: `examples/break_continue_test.ecs` (код 0).
- [x] Пункт 2.4: Оператор явного приведения типов (`as`):
  - Ключевое слово `as`, токен `TokenType.As`, AST-узел `CastExpressionNode`.
  - Синтаксический разбор в Pratt-парсере `Parser.Expressions.cs` с высоким приоритетом (`Precedence.Cast`).
  - Семантическая валидация: `CheckCastExpression()` в `TypeChecker.Expressions.cs` (проверка типов `i32`, `i64`, `f32`, `f64`, тождественные no-op приведения, отсечение несовместимых типов).
  - LLVM инструкции кодогенерации в `LlvmCodeGenerator.Expressions.cs`: `BuildFPToSI`, `BuildSIToFP`, `BuildSExt`, `BuildZExt`, `BuildTrunc`, `BuildFPExt`, `BuildFPTrunc`.
  - Поддержка 64-битного форматирования `%lld` (`rt_to_string_i64`) в интерполяции строк.
  - Сквозной тест: `examples/cast_test.ecs` (код 0).
- [x] Пункт 2.5: Ресурс-геттеры напрямую из `main` (`world.get_Resource()`):
  - Синтаксис прямого вызова геттеров синглтон-ресурсов: `world.get_GameConfig()`.
  - Модульный AST-узел `ResourceGetExpressionNode(Target, ResourceName, Span)`.
  - Модульный парсинг в `Parser.Resources.cs`.
  - Семантическая типизация `CheckResourceGetExpression()` в `TypeChecker.Resources.cs`: проверка `world: World`, поиск объявленного ресурса и возврат `TypeSymbol` структуры.
  - Машинная генерация LLVM IR в `LlvmCodeGenerator.Resources.cs`: обращение к смещению ресурса в `%struct.EcsWorld` через `InBoundsGEP2` и прямая загрузка структуры без накладных расходов.
  - Расширение `MapType` для структурных ресурсов и компонентов, а также безопасная адресация rvalue-структур при цепочечном доступе (`world.get_GameConfig().speed`).
  - Сквозной тест: `examples/resource_main_test.ecs` (код 0).
- [x] Пункт 2.6: Оператор распространения ошибок и опциональных значений (`?`):
  - Постфиксный токен `TokenType.Question` и парсинг в `Parser.Errors.cs` -> `ErrorPropagationExpressionNode(Expr, Span)`.
  - Семантическая валидация в `TypeChecker.Errors.cs`: строгая проверка `Result<T, E>` (с совпадением типа ошибки `E` в объемлющей функции или поддержкой `main()`) и `Option<T>`, распаковка результирующего типа в `T`.
  - Машинная генерация LLVM IR в `LlvmCodeGenerator.Errors.cs`: десугаризация в базовые блоки проверки дискриминанта (`Ok`/`Some` vs `Err`/`None`), ранний возврат `ret` с пробросом ошибки, а в `main()` автоматический вывод ошибки и `ret i32 1`.
  - Сквозной тест: `examples/error_propagation_test.ecs` (код 0).
- [x] Пункт 2.7 (5.2): Промышленный файловый ввод-вывод Big Data (File I/O: 64-bit offsets, atomic locking, binary Vec<u8>):
  - Встроенные функции `file_exists(path)`, `file_read_text(path)`, `file_write_text(path, content)`, `file_append_text(path, content)`, `file_read_bin(path)`, `file_write_bin(path, bytes)`.
  - Модульный фронтенд в `Parser.Files.cs`.
  - Семантическая валидация и типизация в `TypeChecker.Files.cs` с возвратом `bool`, `Result<string, string>`, `Result<bool, string>` и `Result<Vec<u8>, string>`. Полная интеграция с оператором `?`.
  - Поддержка примитивных типов `u8`/`i8` в `TypeSymbol.cs` и `LlvmCodeGenerator.cs` (`MapType`).
  - Низкоуровневая кроссплатформенная генерация C ABI CRT в `LlvmCodeGenerator.Files.cs`:
    * 64-битные смещения: `_fseeki64`/`_ftelli64` (Windows UCRT) и `fseeko`/`ftello` (POSIX) для файлов > 2 ГБ.
    * Атомарная блокировка дескрипторов: `_lock_file`/`_unlock_file` (Windows) и `flockfile`/`funlockfile` (POSIX) для безопасной параллельной записи из систем ECS.
    * Бинарный ввод-вывод: сырое чтение и запись массивов байт `Vec<u8>`.
  - Сквозной тест: `examples/file_io_test.ecs` (проверка текста и бинарных данных, код 0).
- [x] Пункт 2.8 (5.3): Строковые операции Tier 1 (len, contains, starts_with, ends_with, index_of, substring):
  - Встроенные zero-cost строковые методы на базе C CRT ABI: `s.len()`, `s.length()`, `s.contains(sub)`, `s.starts_with(prefix)`, `s.ends_with(suffix)`, `s.index_of(sub)`, `s.substring(start, len)`.
  - Модульный фронтенд в `src/ECSLang.Frontend/Parser.Strings.cs`.
  - Модульный семантический анализ в `src/ECSLang.Semantics/TypeChecker.Strings.cs` (`TryCheckStringMethod`).
  - Низкоуровневая машинная кодогенерация в `src/ECSLang.Codegen.LLVM/LlvmCodeGenerator.Strings.cs`:
    * Вызовы `strlen`, `strstr`, `strncmp`, `strcmp`, `llvm.memcpy`.
    * Branchless вычисление смещения `index_of` через `ptrtoint` + `sub` + `select`.
    * Зажатие границ (clamping) в `substring` против out-of-bounds и отрицательных смещений.
    * Выделение памяти под срез через ECS String Arena (`_arenaEmitter.GetOrCreateArenaAlloc`) с fallback на CRT `malloc`.
  - Сквозной тест: `examples/string_ops_test.ecs` (парсинг логов web-сервера, код 0).
- [x] Пункт 2.9 (9.4): Высокоточные интринсики времени и честный бенчмарк Big Data I/O (stopwatch_start, stopwatch_ms, io_benchmark.ecs):
  - Встроенные zero-cost интринсики таймера: `stopwatch_start(): i64` и `stopwatch_ms(start: i64): f32`.
  - Модульный фронтенд в `src/ECSLang.Frontend/Parser.Time.cs`.
  - Модульный семантический анализ в `src/ECSLang.Semantics/TypeChecker.Time.cs` (`TryCheckTimeCall`).
  - Низкоуровневая машинная кодогенерация в `src/ECSLang.Codegen.LLVM/LlvmCodeGenerator.Time.cs`:
    * Windows: нативные вызовы `QueryPerformanceCounter` и `QueryPerformanceFrequency` (`kernel32.lib`).
    * POSIX: наносекундный монотонный таймер `clock_gettime(CLOCK_MONOTONIC, &ts)`.
  - Поддержка синтаксиса `Result::Ok` и `Result::Err` в семантике и кодогене.
  - Честный сравнительный Big Data бенчмарк: `examples/io_benchmark.ecs` (атомарная запись 1 млн строк в файл и чтение целиком).
- [x] Пункт 2.10 (5.4): Сетевой протокол TCP Framing и Length-Prefix (`std/net.ecs`):
  - Расширение компонента `TcpConnection`: `rx_buffer: Vec<u8>`, `expected_len: i32` (по умолчанию `-1`), `current_offset: i32`.
  - Модернизация `EcsRuntimeEmitter.MapType` для поддержки `Vec<T>` / `List<T>` (`{ i8*, i32, i32 }`) и примитивов `u8`, `i8`, `u16`, `i16` в архетипах и очереди команд.
  - Нативные примитивы framing и буферизации в `src/ECSLang.Codegen.LLVM/LlvmCodeGenerator.Net.cs`:
    * `ecs_net_tcp_send_framed(fd: i32, payload: string) -> i32`: упаковка 4-байтового префикса длины в сетевом порядке байт (`htonl`) и атомарная отправка через сокет.
    * `ecs_net_tcp_recv_append(fd: i32, buf: Vec<u8>, max_len: i32) -> Vec<u8>`: неблокирующее чтение сырых байт с авто-реаллокацией и добавлением в `rx_buffer`.
    * `ecs_net_buffer_read_i32(buf: Vec<u8>, offset: i32) -> i32`: безопасное извлечение 32-битного целого с конвертацией из сетевого порядка байт (`ntohl`).
    * `ecs_net_buffer_extract_str(buf: Vec<u8>, offset: i32, len: i32) -> string`: безопасное извлечение среза строки с завершающим `\0`.
    * `ecs_net_buffer_drain(buf: Vec<u8>, count: i32) -> Vec<u8>`: `memmove`-сдвиг буфера с сохранением выделенной емкости без повторных аллокаций.
  - Семантическая типизация в `TypeChecker.Expressions.cs` для built-in функций framing.
  - Стандартная библиотека `std/net.ecs` и `std/net/systems.ecs`:
    * `TcpReceiverSystem`: цикл распаковки пакетов в `rx_buffer` с защитой от склеивания (sticky packets) и фрагментации.
    * `OutboundSendSystem`: автоматическая длина фрейма для TCP и прозрачная маршрутизация для UDP-дейтаграмм.
  - Сквозной тест: `examples/net_framing_test.ecs` (проверка распаковки склеенных пакетов, код 0).

- [x] Пункт 2.11 (9.1): Автопланировщик пула потоков `parallel auto` внутри стадий конвейеров (Topological DAG System Scheduler):
  - Ключевое слово `auto`, токен `TokenType.Auto`, AST-узел `ParallelAutoBlockNode(IReadOnlyList<SystemCallAction> Systems, SourceSpan Span)`.
  - Модульный фронтенд в `src/ECSLang.Frontend/Parser.Pipelines.cs`: парсинг `parallel auto { System1; System2; ... }` внутри стадий `stage Name { ... }`.
  - Модульный семантический анализ в `src/ECSLang.Semantics/TypeChecker.Pipelines.cs`:
    * Построение топологического графа зависимостей (DAG) между системами на основе конфликтов `write-write`, `write-read` и `read-write` для компонентов и ресурсов (`SystemSymbol.HasConflictWith`).
    * Автоматическое разбиение систем на независимые параллельные слои (батчи): $\text{Layer}(i) = \max_{j < i, \text{conflict}(j, i)} (\text{Layer}(j) + 1)$.
    * Гарантия Data-Race Free исполнения без необходимости ручной расстановки барьеров `sync;`.
  - Низкоуровневая машинная кодогенерация в `src/ECSLang.Codegen.LLVM/LlvmCodeGenerator.Pipelines.cs`:
    * Параллельная подача задач каждого батча в Win32 ThreadPool (`CreateThreadpoolWork`, `SubmitThreadpoolWork`).
    * Аппаратный барьер ожидания завершения слоя (`WaitForThreadpoolWorkCallbacks`, `CloseThreadpoolWork`) перед переходом к следующему батчу.
    * Оптимизация одиночных систем в батче с прямым вызовом `@system_*` без оверхеда пула потоков.
- [x] Пункт 2.12 (6): Скорость компиляции и метрики фаз (Метрика фаз + lld-link по умолчанию в dev-профиле с умным fallback):
  - Метрика фаз компиляции в `src/ECSLang.CLI/Program.cs`:
    * Замер времени (`Stopwatch`) четырех независимых фаз: Frontend (парсинг и загрузка модулей), Semantics (проверка типов и анализ DAG), Codegen (генерация LLVM IR и объектного файла), Linker (компоновка бинарника) и Total Time.
    * Вывод форматированной ASCII-таблицы метрик в консоль при сборке и запуске проектов.
  - Идемпотентность семантического анализатора: `TypeChecker.IsChecked` предотвращает повторный проход при передаче готовых символов в `LlvmCodeGenerator`.
  - Модульный компоновщик `src/ECSLang.Toolchain/MsvcLinker.Lld.cs`:
    * Метод `TryResolveLldLink()` с динамическим обнаружением многопоточного линкера `lld-link.exe` в PATH, `LLVM_HOME`/`LLVM_PATH`, стандартных путях Program Files и встроенных LLVM-пакетах Visual Studio.
    * Дефолтное переключение на `lld-link.exe` в dev-профиле (без оптимизаций O3).
    * Автоматический тихий fallback на стандартный `link.exe` от MSVC при отсутствии `lld-link.exe`, гарантирующий 100% работоспособность на любых окружениях без вылетов и варнингов.
    * Генерация отладочной информации `/DEBUG` и PDB файлов строго при наличии явного флага `-g`, исключающая накладные расходы на диск в цикле быстрой разработки (F5).
- [x] Пункт 2.13 (9.3): Неигровой Killer App — Высокопроизводительный In-Memory OLAP Big Data Analytics Engine (`examples/olap_bigdata_analyzer.ecs`):
  - Модель данных колоночного хранения (Columnar SoA Layout):
    * Компоненты: `LogTimestamp { time: string }`, `LogLevel { is_info: bool, is_error: bool }`, `HttpMethod { is_get: bool }`, `ResponseTime { ms: f32 }`, `StatusCode { code: i32 }`.
    * Глобальные изолированные ресурсы: `ErrorStats`, `LatencyStats`, `TrafficStats`, `AnalyticsResult`.
  - Системы аналитики и автопланирование:
    * `FilterErrorsSystem` (сканирование ошибок, инкремент счетчика).
    * `MetricsAggregatorSystem` (суммирование времени ответа сервера).
    * `TrafficAnalyzerSystem` (анализ соотношения HTTP-методов и статусов ответов).
    * Конвейер `OlapQueryPipeline` со стадией `stage Analysis { parallel auto { ... } }`, автоматически распараллеливающей все 3 системы на воркеры пула потоков без блокировок и гонок данных.
  - Расширение строковых операций Tier 1 (`TypeChecker.Strings.cs` & `LlvmCodeGenerator.Strings.cs`):
    * Поддержка перегрузки `string.index_of(sub, start_index: i32)` для быстрого $O(1)$-индексированного последовательного парсинга мегабайтных логов без создания промежуточных копий остатка строки.
  - Сквозной сценарий Big Data:
    * Генерация 50 000 строк реального выхлопа веб-сервера на диск через 64-битный файловый I/O (`file_write_text` / `file_append_text`).
    * Монолитная загрузка файла в память (`file_read_text`), парсинг строк на лету и спавн 50 000 сущностей в колоночную SoA-память мира.
    * Выполнение параллельного конвейера OLAP-аналитики: обработка 50 000 записей за **1.46 мс** (пропускная способность **34.22 миллиона записей в секунду**!).
    * Вывод форматированной ASCII-таблицы бизнес-метрик и телеметрии.
- [x] Пункт 2.14: Оптимизация ядра ECS — Bulk Spawn (`world.spawn_with(Comp1(...), Comp2(...), ...)`):
  - Ликвидация 250 000 миграций архетипов: прямое создание сущности в целевом архетипе без промежуточных перемещений и swap-remove.
  - AST узел: `BulkSpawnExpressionNode(Target, Components, Span)`.
  - Модульный фронтенд (`Parser.BulkSpawn.cs`): перехват метода `spawn_with` над миром/командами.
  - Семантическая валидация (`TypeChecker.BulkSpawn.cs`): проверка принадлежности компонентов, соответствия типов аргументов конструкторов и отсутствия дубликатов; возвращаемый тип `Entity`.
  - Высокопроизводительный кодогенератор (`LlvmCodeGenerator.BulkSpawn.cs`):
    * Вычисление целевой маски архетипа на этапе компиляции / исполнения.
    * Однократный вызов `world_alloc_entity` + `world_get_or_create_archetype` + `world_grow_archetype`.
    * Прямая запись данных компонентов в столбцы SoA (`arch.columns[k][row]`).
  - Результаты на бенчмарке 50 000 записей OLAP:
    * Количество миграций памяти снижено с 250 000 до 0!
    * Время фазы параллельного OLAP-запроса: **0.37 мс**!
    * Пропускная способность: **136.46 млн записей / сек**!
    * Общее сквозное время упало с 47 сек до **5.04 сек**!
- [x] Пункт 2.15: Zero-Copy String Views (`str_view`) и оптимизация парсинга строк:
  - Нативный тип данных `str_view` в системе типов (`TypeSymbol.StrView`) и LLVM IR (`{ i8* ptr, i32 len }`).
  - Методы `string.view_substring(start, len): str_view` и `str_view.view_substring(start, len): str_view`: чистая адресная арифметика GEP и клемпинг без системных аллокаций (`malloc`) и копирования памяти (`memcpy`).
  - Метод `str_view.len` / `str_view.len()`: мгновенное чтение 32-битного поля длины за $O(1)$.
  - Гранично-безопасные методы `contains(needle)` и `starts_with(prefix)` для `str_view` через низкоуровневые рантайм-хелперы `@rt_str_view_contains` и `@rt_str_view_starts_with` на базе `strncmp`.
  - Преобразование `str_view.to_string(): string` и прямая поддержка интерполяции строк (`$"..."`) с авто-нультерминацией при явном форматировании.
  - Полноценная интеграция `str_view` как гражданина первого класса в ECS: поддержка в компонентах (`component LogTimestamp { time: str_view }`) и колоночных SoA-чанк таблицах архетипов.
  - Ликвидация 250 000 промежуточных аллокаций памяти кучи при инжесте Big Data логов в флагманском In-Memory OLAP движке.
- [x] Пункт 2.16: Официальный релиз SDK v0.1.0 alpha и упаковка дистрибутива:
  - Автоматизированный скрипт сборки SDK (`publish_sdk.bat`):
    * Вызов `dotnet publish src/ECSLang.CLI` в конфигурации Release с флагом Native AOT (`-r win-x64 --self-contained /p:PublishAot=true`).
    * Генерация компактного (3.6 МБ) нативного машинного бинарника `ECSLang.CLI.exe` и алиасов `ecslang.exe`, `ecs.exe` с `libLLVM.dll` без зависимостей от сторонних рантаймов.
    * Упаковка стандартной библиотеки в `dist/ecslang-sdk/std/` (строго файлы `.ecs`).
    * Упаковка проверенных золотых примеров и бенчмарков (`examples/io_benchmark_precise.ecs`, `examples/olap_bigdata_analyzer.ecs`, `examples/01_hello_world.ecs`, `examples/14_pure_dod_network.ecs`, `examples/18_arcade_void_defender.ecs`).
  - Витрина репозитория GitHub (`README.md`):
    * Позиционирование языка: Pure ECS/DOD, бэкенд LLVM 20.1, 0% GC, 0% OOP overhead, нативная скорость.
    * Таблица результатов честного Apples-to-Apples бенчмарка против C# (.NET 9) на старом железе Sandy Bridge (1.7x быстрее на записи, 2.6x на чтении, 78x меньший оверхед памяти).
    * Сводка In-Memory OLAP архитектуры: 136+ млн записей/сек, 0 миграций памяти, zero-copy `str_view`.
    * Лаконичные демонстрационные листинги кода и руководство Quick Start.
- [x] Пункт 2.17: Двухкомпонентная модель монетизации и Open-Core архитектура экосистемы:
  - Правовая и лицензионная модель (WinRAR-style & Open-Core):
    * Бессрочное бесплатное использование (100% Free Community & Indie License) для физических лиц, инди-разработчиков, студентов, исследователей и компаний с годовым оборотом до 2 000 000 рублей (или $25,000 USD).
    * Обязательное коммерческое лицензирование (Commercial Enterprise License) для корпораций и организаций с оборотом или финансированием от 2 000 000 рублей (или $25,000 USD) на рабочие места разработчиков и серверные инсталляции.
    * Архитектурное разделение Open-Core: ядро компилятора и базовые стандартные библиотеки (`std/`) открыты и бесплатны, а специализированные Enterprise-модули распределенной синхронизации кластеров ИИ (Distributed Multi-Node AI Cluster Sync, Enterprise SLA) развиваются как проприетарные закрытые компоненты.

- [x] Пункт 2.18: Рефакторинг ядра и физическая изоляция Code Bloat (Шаг 1 - ECSLang.Codegen.LLVM):
  - Декомпозиция монолитного `LlvmCodeGenerator.Expressions.cs` (2 692 -> 1 045 строк) с физическим выделением `partial class` модулей:
    * `LlvmCodeGenerator.TaggedUnions.cs`: генерация конструкторов `Some`, `None`, `Ok`, `Err` и методов `is_some`, `is_none`, `unwrap`, `unwrap_or`, `is_ok`, `is_err`, `unwrap_err`.
    * `LlvmCodeGenerator.Closures.cs`: компиляция `LambdaExpression`, косвенных вызовов `IndirectCallExpression` и вызовов переменных-замыканий fat pointer `{ fn, env }`.
    * `LlvmCodeGenerator.Collections.cs`: литералы массивов, индексация `IndexExpression`, конструкторы и методы `Vec`/`List` и `Map`/`HashMap` (`push`, `pop`, `len`, `clear`, `get`, `insert`, `remove`, `contains`, `find`, `for_each`, `map`, `filter`, `any`, `all`).
    * `LlvmCodeGenerator.Raylib.cs`: единая точка генерации всех Raylib 2D-вызовов (окно, рисование, мышь/клавиатура, текстуры, звук, камера).
  - Декомпозиция монолитного `EcsRuntimeEmitter.Archetypes.cs` (1 806 -> 1 366 строк) с физическим выделением `partial class` модулей:
    * `EcsRuntimeEmitter.Commands.cs`: отложенные буферы команд (`world_cmd_ensure_cap`, `world_cmd_spawn`, `world_cmd_despawn`, `world_cmd_set_*`, `world_cmd_add_*`, `world_cmd_remove_*`, `world_apply_commands`).
    * `EcsRuntimeEmitter.Events.cs`: двухбуферные очереди событий (`world_emit_*`, `world_swap_events`).
  - Полная верификация сквозной сборки `dotnet build` (0 предупреждений, 0 ошибок) и валидация рантайма на примерах `examples/09_ecs_command_buffer.ecs` и `examples/10_ecs_events_and_observers.ecs` (Exit Code 0).
- [x] Пункт 2.19: Рефакторинг ядра и физическая изоляция Code Bloat (Шаг 2 - ECSLang.Semantics):
  - Декомпозиция монолитного `TypeChecker.Expressions.cs` (1 283 -> 575 строк) с физическим выделением `partial class` модулей:
    * `TypeChecker.Closures.cs`: семантический анализ `LambdaExpression`, косвенных вызовов `IndirectCallExpression` и алгоритм детекции захваченных переменных `FindCaptures`.
    * `TypeChecker.Collections.cs`: типизация `ArrayLiteralExpression`, `IndexExpression`, конструкторов и встроенных методов коллекций `Vec<T>` и `Map<K, V>`.
    * `TypeChecker.TaggedUnions.cs`: типизация конструкторов `Some`, `None`, `Ok`, `Err` и методов `Option<T>` / `Result<T, E>`.
    * `TypeChecker.Raylib.cs`: семантическая валидация вызовов Raylib API.
  - Исправление резолюции методов `get_*` на структурах при наличии синтаксического сахара геттеров ресурсов мира (`ResourceGetExpressionNode` fallback).
  - Успешная сборка `dotnet build` (0 варнингов, 0 ошибок) и прохождение тестов `06_dynamic_collections.ecs`, `07_closures_and_generics.ecs` и `resource_main_test.ecs` (Exit Code 0).
- [x] Пункт 2.20: Актуализация документации и устранение расхождений (Шаг 3):
  - Исправление незакрытого блока кода в `REFERENCE.md` в разделе управления ареной строк мира (`world.reset_string_arena()`).
  - Устранение дублирующейся нумерации разделов `## 15.` и `### 16.6` в `REFERENCE.md`: профайлер F1 перенесен в подраздел `16.8`, встроенная математика пронумерована `16.7`.
  - Добавление полного руководства по Дженерикам и Трейтам (`Pair<T, U>`, `show<T: Printable>`) в раздел `7.1` документа `REFERENCE.md`.
  - Документирование атомарного массового спавна `world.spawn_with` (Bulk Spawn) в разделе `10` документа `REFERENCE.md`.
  - Восстановление пропущенного раздела 26 (`Монолитная арена строк и управление памятью нулевой фрагментации`) в `ARCHITECTURE.md`.
  - Создание модуля стандартной библиотеки `std/math.ecs` с константами `PI`, `TAU`, `HALF_PI`, `DEG2RAD`, `RAD2DEG`, `E` (типы `f32` и `f64`).
  - Поддержка неявного авто-приведения вещественных литералов `f32` к типу `f64` при объявлении compile-time констант в `TypeChecker.Declarations.cs`.
- [x] Пункт 2.21: Аудит инвариантов корректности (Директива A-1, Уровень P0, Пункт A1):
  - Запрет воскрешения мертвых сущностей в `EcsRuntimeEmitter.Archetypes.cs`:
    * В `world_set_*` и `world_add_*` устранено авто-назначение `assign_a0` при отрицательном индексе архетипа `curArchIdxRaw < 0`.
    * Добавлен детерминированный аварийный выход с выводом сообщения об ошибке `[ECS Error] Attempted to mutate despawned or dead entity with world_set_*` и кодом завершения 1.
    * В `world_remove_*` добавлена аналогичная проверка смертности сущности перед доступом к таблицам архетипов.
  - Добавлен сквозной инвариантный тест `tests/a1_no_resurrect_dead_entity.ecs`: сценарий `spawn -> despawn -> set_Position` завершается контролируемой ошибкой без затирания памяти.
  - Успешная сборка `dotnet build` (0 errors, 0 warnings) и 100% прогон всех 32 примеров из `examples/` (0 failures).

### [ ] Этап 34: Комплексная демонстрационная экосистема (Универсальность ECSLang)
- [ ] Игровой проект: расширенный "Void Defender" с частицами, звуками, музыкой и оверлеем профайлера.
- [ ] GUI-приложение: редактор уровней или инспектор сцены на ECS GUI компонентах.
- [ ] Сетевой сервис: клиент-серверный чат / симуляция с сетевыми ECS-компонентами.
- [ ] Сводные бенчмарки и публикация документации языка.



