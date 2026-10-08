# ECSLang: Мастер-план разработки (ROADMAP)

Единый источник истины по текущему состоянию и оставшимся этапам разработки подсистем компилятора ECSLang.

---

## 1. Состояние проекта (Аудит Фазы 0)

| Пункт / Фича | Статус | Доказательство в кодовой базе |
|---|---|---|
| **Точка отката Шага 1** | **ВЫПОЛНЕНО** | Тег `a7-step1-green` зафиксирован на коммите `8ffa26e` и запушен в `origin` |
| **A1: Защита от воскрешения** | **ВЫПОЛНЕНО (Шаг P0.1)** | Разграничены состояния: `-1` pending, `-2` dead в `entity_arch` и `entity_row`. Выделен хелпер `@rt_panic`. Инвариантный тест `tests/verify_a1_invariant.ps1` подтверждает код возврата 1 и диагностику ошибки |
| **A2: Лимит 64 компонентов** | **ВЫПОЛНЕНО** | `TypeChecker.cs:188-199` (`ValidateComponentLimit`, ошибка компиляции при >64) |
| **A3: Синхронизация `world_emit_*`** | **ВЫПОЛНЕНО (Шаг P0.3)** | Добавлено отдельное поле `emit_lock: ptr` (поле 11 `%struct.EcsWorld`). Критическая секция `world_emit_*` и `world_swap_events` синхронизирована. Уровень 2 и 3 доказан тестом `tests/verify_a3_emit_lock_invariant.ps1` |
| **A4: Запрет `world.*` в системах** | **ОТСУТСТВУЕТ** | `TypeChecker.Declarations.cs:356` объявляет `world` в скоупе системы, `TypeChecker.Expressions.cs:421` разрешает любые вызовы кроме `Commands` |
| **A5: Запрет escape-замыканий** | **ОТСУТСТВУЕТ** | `TypeChecker.Closures.cs:9-56` и `TypeChecker.Statements.cs:48-60` не валидируют возврат замыканий с захватом стека |
| **A6: Guards (bounds + liveness)** | **ЧАСТИЧНО** | Liveness (`< 0`) есть в `set_/add_/remove_/has_`, но bounds check (`e < 0 \|\| e >= entity_count`) отсутствует во всех функциях манипуляции |
| **B1: `pause` в POSIX-спинлоке** | **ВЫПОЛНЕНО (Шаг P0.2)** | Встроен `llvm.x86.sse2.pause` (x86_64) и `llvm.aarch64.hint(1)` (Arm64) в `spin_backoff` базовый блок `ecs_spin_acquire`. Уровень 3 доказан тестом `tests/verify_b1_pause_invariant.ps1` |
| **A7 У1: Сьют Ур. 1 с исполнением** | **ВЫПОЛНЕНО** | `tests/run_all_examples.ps1` (29 собрано, 23 выполнено ExitCode 0, 6 документированных GUI/Network пропусков; 3 тяжелых I/O-файла удалены по указанию) |
| **A7 У2: Perf particles_100k** | **БЕЙЗЛАЙН ЗАФИКСИРОВАН** | `tests/particles_100k.ecs` в Release -O3: спавн 100k = ~18.3 мс, 100 тиков = ~74 мс. Сравнение до/после Шага 1 обязательно на шаге A7.Ф через checkout родительского коммита |
| **A7 У3: Гистограмма диффов Шага 1** | **ВЫПОЛНЕНО** | `tests/diff_histogram.ps1` категоризировал 6 эталонов; 3 хунка задокументированы |
| **A7 У4: Инвариант word-0 в доках** | **ВЫПОЛНЕНО** | `ARCHITECTURE.md:158-162` непосредственно внутри раздела Archetype |
| **A7 У5: Распределение нерефакторенных строк** | **ВЫПОЛНЕНО** | Строки №10–18 -> Шаг 2; №20–23 -> Шаг 3; №6, 8, 9, 24–26 -> Шаг 4 |
| **Дрель Д1 (сигнализация IR-диффа)** | **ВЫПОЛНЕНО** | Мутация 1 байта в `cast_test.ll` -> FAIL (код 1) -> откат -> PASS (6/6 EXACT MATCH) |
| **Дрель Д2 (сигнализация ABI-ассерта)** | **ВЫПОЛНЕНО** | Мутация ожидаемого размера (24 -> 32) -> падение компилятора (`InvalidOperationException`) -> откат -> PASS |

---

## 2. Пошаговый план работ

### Блок 0: Закрытие критических дефектов безопасности ядра (Pre-A7)
*Эти пункты правят те же функции генератора рантайма, что и A7.2–A7.4. Выполнение ДО снятия лимита компонентов исключает повторный рефакторинг.*

#### Шаг P0.1 — Выделение `rt_panic` и разграничение состояний сущности (A1-fix)
- **Объём**: Ввести хелпер `rt_panic(ptr msg)` в кодогене. Разграничить состояния: `-1` = unassigned/pending allocation, `-2` = dead/despawned. Мутация сущности со статусом `-2` вызывает `rt_panic` с диагностикой `[ECS Error] Attempted to mutate despawned or dead entity`.
- **Файлы**: `src/ECSLang.Codegen.LLVM/EcsRuntimeEmitter.Archetypes.cs`.
- **Критерий приёмки**: Уровень 2 (инвариантный тест `tests/a1_no_resurrect_dead_entity.ecs` подтверждает аварийную остановку).
- **Статус**: **ВЫПОЛНЕНО** (коммит P0.1; Уровень 2 подтвержден `tests/verify_a1_invariant.ps1`; Уровень 3 строгий IR-паритет 6/6; Уровень 1 23/23).
- **Шаблон коммита**: `fix(runtime): introduce rt_panic and distinguish pending (-1) vs dead (-2) entity states`

#### Шаг P0.2 — Аппаратный `pause` в POSIX-спинлоке (B1-fix)
- **Объём**: Встроить инструкцию `pause` (`llvm.x86.sse2.pause` или inline asm / rep;nop) в тело цикла `spinLoopBB` функции `ecs_spin_acquire`.
- **Файлы**: `src/ECSLang.Codegen.LLVM/EcsRuntimeEmitter.Archetypes.cs`.
- **Критерий приёмки**: Уровень 3 (IR-дифф: наличие инструкции `pause` в цикле ожидания).
- **Статус**: **ВЫПОЛНЕНО** (коммит P0.2; Уровень 3 IR-тест `tests/verify_b1_pause_invariant.ps1`; Уровень 3 строгий IR-паритет 6/6; Уровень 1 23/23).
- **Шаблон коммита**: `fix(sync): insert pause instruction into POSIX spinlock backoff loop`

#### Шаг P0.3 — Синхронизация `world_emit_*` через отдельный `emit_lock` (A3-fix)
- **Объём**: Добавить ОТДЕЛЬНОЕ поле `emit_lock: ptr` (Win32 SRWLOCK / POSIX спинлок) в структуру `%struct.EcsWorld` (поле 11). Смешивание с `cmd_lock` запрещено, так как эмит содержит `realloc`. Обернуть критическую секцию `world_emit_*` (чтение `wcount`, реаллокация буфера, запись события, инкремент `wcount`) в захват и освобождение `emit_lock`.
- **Файлы**: `src/ECSLang.Codegen.LLVM/EcsRuntimeEmitter.cs`, `EcsRuntimeEmitter.Events.cs`.
- **Критерий приёмки**: Уровень 3 (IR-дифф: наличие acquire/release вокруг записи событий) + Уровень 1 (`10_ecs_events_and_observers.ecs`).
- **Статус**: **ВЫПОЛНЕНО** (коммит P0.3; Уровень 2 и 3 IR-тест `tests/verify_a3_emit_lock_invariant.ps1`; Уровень 3 строгий IR-паритет 6/6; Уровень 1 23/23).
- **Шаблон коммита**: `fix(events): synchronize world_emit_* under dedicated world emit_lock`

#### Шаг P0.4 — Валидация границ сущности и liveness guards во всех операциях (A6-fix)
- **Объём**: Добавить проверки `e < 0 || e >= world.entity_count` (Out of Bounds) и `world.entity_arch[e] < 0` (Dead/Pending) в начало `world_set_*`, `world_add_*`, `world_remove_*`, `world_has_*`. При выходе за границы — вызов `rt_panic`.
- **Файлы**: `src/ECSLang.Codegen.LLVM/EcsRuntimeEmitter.Archetypes.cs`.
- **Критерий приёмки**: Уровень 2 (тест с передачей невалидного `entity_id` завершается диагностируемой ошибкой, не SegFault).
- **Шаблон коммита**: `fix(runtime): enforce entity bounds and liveness guards across all world operations`

#### Шаг P0.5 — Белый список вызовов `world.*` внутри систем (A4-fix)
- **Объём**: В `TypeChecker.Declarations.cs` и `TypeChecker.Expressions.cs` при проверке тела системы применить строгий белый список: разрешен ТОЛЬКО `world.emit_*`. Любое обращение к другим методам мира (`spawn`, `despawn`, `set_*`, `add_*`, `remove_*`, `has_*`, `get_*`, `find`, `sort_hierarchy`, `swap_events`, `apply_commands`, `reset_string_arena`) вызывает ошибку компиляции с подсказкой использовать `cmd.*`.
- **Файлы**: `src/ECSLang.Semantics/TypeChecker.Declarations.cs`, `TypeChecker.Expressions.cs`.
- **Критерий приёмки**: Уровень 2 (негативный тест попытки вызова `world.set_*` или `world.has_*` внутри системы падает с диагностической ошибкой компиляции).
- **Шаблон коммита**: `feat(semantics): whitelist world methods inside system bodies to emit_* only`

#### Шаг P0.6 — Запрет escape-замыканий на уровне типов (A5-fix)
- **Объём**: Правило уровня ТИПОВ: тип capturing-closure запрещен в: возвращаемом типе функций (`return`), полях `struct`/`component`/`resource`, типах-аргументах `Vec<T>` / `Map<K, V>`, параметрах пользовательских функций. Разрешен ТОЛЬКО как аргумент встроенных методов коллекций (`for_each`, `map`, `filter`, `any`, `all`, `find`). Замыкания без захвата (чистые функции) разрешены без ограничений.
- **Файлы**: `src/ECSLang.Semantics/TypeChecker.Closures.cs`, `TypeChecker.Statements.cs`, `TypeChecker.Expressions.cs`.
- **Критерий приёмки**: Уровень 2 (негативные тесты возврата и сохранения capturing-closure падают на этапе типизации).
- **Шаблон коммита**: `feat(semantics): prohibit escaping capturing-closure types across signatures and collections`

---

### Блок A7: Снятие предела 64 компонентов (Многословные маски)

#### Шаг A7.2 — Расчет целевой маски через `wordIdx` / `bitIdx` в `set_`, `add_`, `remove_`, `has_`
- **Объём**: Замена хардкода слова 0. Реализация `wordIdx = compId >> 6`, `bitIdx = compId & 63`. Пословное копирование маски, вычисление целевого слова через `OR` / `AND-NOT`, fast-path проверка наличия компонента через целевое слово.
- **Файлы**: `src/ECSLang.Codegen.LLVM/EcsRuntimeEmitter.Archetypes.cs`.
- **Критерий приёмки**: Уровень 3 (рефактор-режим `verify_golden_ir.ps1`, ребейзлайн) + Уровень 1 (`run_all_examples.ps1`).
- **Шаблон коммита**: `feat(codegen): implement multi-word target mask calculation in world_set, add, remove, has (Step 2)`

#### Шаг A7.3 — Многословные маски в `despawn`, `grow_archetype`, `spawn_with` и глобальный `@_ecs_zero_mask`
- **Объём**: Внедрение глобальной константы `@_ecs_zero_mask = internal constant [WORDS x i64] zeroinitializer` вместо per-call `alloca` в `ecs_create_world` и `world_assign_a0`. Обновление `spawn_with` (`LlvmCodeGenerator.BulkSpawn.cs`), `despawn` swap-copy и `world_grow_archetype` на пословную адресацию.
- **Файлы**: `src/ECSLang.Codegen.LLVM/EcsRuntimeEmitter.cs`, `EcsRuntimeEmitter.Archetypes.cs`, `LlvmCodeGenerator.BulkSpawn.cs`.
- **Критерий приёмки**: Уровень 3 (дифф-гистограмма + strict match после ребейзлайна) + Уровень 1 (`particles_100k`).
- **Шаблон коммита**: `feat(codegen): migrate despawn, bulk spawn and runtime zero mask to multi-word arrays (Step 3)`

#### Шаг A7.4 — Многословная фильтрация запросов систем (`query`)
- **Объём**: Представление `allMask`, `anyMask`, `noneMask` как `[WORDS x i64]`. Пословное сопоставление в цикле фильтрации архетипов с пропуском нулевых слов (`zero-word skip`).
- **Файлы**: `src/ECSLang.Codegen.LLVM/EcsRuntimeEmitter.cs`, `LlvmCodeGenerator.Pipelines.cs`.
- **Критерий приёмки**: Уровень 3 (сохранение корректности всех 6 golden IR) + Уровень 1 (`11_ecs_archetypes_and_filters.ecs`).
- **Шаблон коммита**: `feat(codegen): implement multi-word query filtering with zero-word skip (Step 4)`

---

### 🛑 КПП-1: КАНАРЕЙКА (Валидация многословности > 64 компонентов)
- **Действие**: Временное повышение порога `TypeChecker` до 128. Создание `tests/canary_70_components.ecs` (70 компонентов, $WORDS = 2$, переходы между компонентами с `id < 64` и `id >= 64`, фильтрация `query`, `spawn_with`, `despawn`).
- **Критерий приёмки**: Уровень 1 (компиляция и исполнение с кодом возврата 0 и валидацией инвариантов).
- **Остановка**: Доклад Архитектору результатов канарейки. Продолжение по команде «продолжай».

---

### 🛑 КПП-2: Снятие лимита компонентов (Архитектурное решение)

#### Шаг A7.5 — Постоянное снятие предела 64 компонентов
- **Объём**: Замена лимита 64 в `TypeChecker.cs` на потолок 65 535 компонентов с предупреждением компилятора при > 1024. Перевод `tests/canary_70_components.ecs` в статус 7-го эталона `tests/golden_ir/over_64_components.ll`.
- **Файлы**: `src/ECSLang.Semantics/TypeChecker.cs`, `tests/verify_golden_ir.ps1`, `tests/golden_ir/over_64_components.ll`.
- **Критерий приёмки**: Уровень 3 (7/7 золотых эталонов в strict mode).
- **Шаблон коммита**: `feat(semantics): permanently lift 64-component limit to 65535 and add 70-comp golden IR (Step 5)`

#### Шаг A7.6 — Обновление профайлера F1
- **Объём**: Вывод многословной битовой маски в оверлее профайлера F1 в шестнадцатеричном пословном формате `[0x... 0x...]`.
- **Файлы**: `src/ECSLang.Codegen.LLVM/EcsRuntimeEmitter.Profiler.cs`.
- **Критерий приёмки**: Уровень 1 (`examples/18_arcade_void_defender.ecs`).
- **Шаблон коммита**: `feat(profiler): render multi-word archetype masks in F1 telemetry HUD (Step 6)`

#### Шаг A7.Ф — Финализация серии A7 и граничное тестирование
- **Объём**: Синтетический стресс-тест на границах слов: 63, 64, 65, 127, 128, 129 компонентов. Финальный замер `particles_100k`. Итоговый аудит соответствия `ARCHITECTURE.md`.
- **Файлы**: `tests/boundary_components_test.ecs`, `ARCHITECTURE.md`.
- **Критерий приёмки**: Уровень 4 (граничные стресс-тесты пройдены с кодом 0).
- **Шаблон коммита**: `test(stress): verify component boundary masks (63/64/65/127/128/129) and update architecture docs`

---

### Блок B: Оптимизации ядра и платформенная переносимость

#### Шаг B3 — Настраиваемое форматирование `f32` / `f64`
- **Объём**: Замена жесткого хардкода `%.2f` в интерполяции строк и `println` на настраиваемое форматирование с дефолтом `%g` / `%f`.
- **Файлы**: `src/ECSLang.Codegen.LLVM/LlvmCodeGenerator.Expressions.cs`.
- **Критерий приёмки**: Уровень 1 (`02_variables_and_math.ecs`).
- **Шаблон коммита**: `feat(codegen): configurable floating point formatting in string interpolation`

#### Шаг B6 — Флаг компилятора `--pause-on-exit`
- **Объём**: Исключение безусловного авто-вызова `@getchar()` перед выходом из `main`. Генерация ожидания нажатия клавиши строго при наличии CLI-флага `--pause-on-exit`.
- **Файлы**: `src/ECSLang.Codegen.LLVM/LlvmCodeGenerator.cs`, `src/ECSLang.CLI/Program.cs`.
- **Критерий приёмки**: Уровень 1 (чистый headless запуск без зависаний).
- **Шаблон коммита**: `feat(cli): gate console pause-on-exit behind explicit compiler flag`

#### Шаг B4 — Хэш-индекс архетипов (`mask -> archIdx`)
- **Объём**: Интеграция быстрого поиска архетипа через встроенную хэш-таблицу вместо линейного сканирования 16 архетипов. Бенчмарк времени спавна и миграций до и после.
- **Файлы**: `src/ECSLang.Codegen.LLVM/EcsRuntimeEmitter.Archetypes.cs`.
- **Критерий приёмки**: Уровень 1 (perf-замер подтверждает ускорение при росте числа архетипов > 32).
- **Шаблон коммита**: `perf(runtime): accelerate archetype lookup via embedded mask hash index`

#### Шаг B2 — Рециклинг Entity ID (Dense Free-List)
- **Объём**: Замена монотонного `world.next_entity_id++` на dense free-list повторного использования идентификаторов деспавненных сущностей с поколенческой защитой (generation counter) против ABA-проблемы.
- **Файлы**: `src/ECSLang.Codegen.LLVM/EcsRuntimeEmitter.Archetypes.cs`.
- **Критерий приёмки**: Уровень 2 (инвариантный тест переиспользования id с валидацией поколений).
- **Шаблон коммита**: `feat(runtime): introduce dense free-list with generational entity id recycling`

#### Шаг B5 — Полноценный POSIX-пул потоков для `parallel`
- **Объём**: Реализация нативного пула воркеров на `pthread_create` / `pthread_cond` для таргетов Linux и macOS (либо явная диагностическая ошибка компиляции `parallel stages not supported on non-Windows targets yet` до реализации).
- **Файлы**: `src/ECSLang.Codegen.LLVM/LlvmCodeGenerator.Pipelines.cs`.
- **Критерий приёмки**: Уровень 1 (сборка и запуск `13_multithreading_benchmark.ecs` под Linux x86_64).
- **Шаблон коммита**: `feat(runtime): cross-platform POSIX thread pool for parallel pipeline stages`

#### Шаг D2 — Сквозной ASan-прогон всех тестов и примеров
- **Объём**: Сборка с флагом `-fsanitize=address` под Clang/MSVC, полный прогон сьюта Уровня 1.
- **Файлы**: `tests/run_asan_suite.ps1`.
- **Критерий приёмки**: Уровень 4 (0 утечек памяти, 0 use-after-free, 0 buffer overflows).
- **Шаблон коммита**: `ci(sanitizer): integrate address sanitizer stress verification suite`

---

### 🛑 КПП-3: Синхронизация публичной документации (Блок C)

#### Шаг C1 — Реалистичный аудит `README.md`
- **Объём**: Приведение терминологии в соответствие с фактической реализацией: исключение необоснованных утверждений ("lock-free", "0% OOP bloat", "enterprise"), точное описание модели многопоточности и границ применимости.
- **Файлы**: `README.md`.
- **Критерий приёмки**: Рецензия Главного Архитектора.
- **Шаблон коммита**: `docs(readme): calibrate technical terminology and performance claims`

#### Шаг C2 — Устранение архитектурного дрейфа `ARCHITECTURE.md`
- **Объём**: Синхронизация структур данных, удаление упоминаний нереализованных абстракций ("чанки" без реализации), актуализация лейаутов мира и очередей событий.
- **Файлы**: `ARCHITECTURE.md`.
- **Критерий приёмки**: Рецензия Главного Архитектора.
- **Шаблон коммита**: `docs(architecture): eliminate documentation drift against physical runtime code`

#### Шаг C3 — Публикация честных бенчмарков со свободными исходниками
- **Объём**: Размещение в репозитории воспроизводимых бенчмарков с идентичной алгоритмикой для ECSLang, C и Rust+Flecs.
- **Файлы**: `benchmarks/`.
- **Критерий приёмки**: Уровень 1 (все бенчмарки собираются и запускаются на машине пользователя).
- **Шаблон коммита**: `bench(public): reproducible cross-language benchmarks with identical algorithms`
