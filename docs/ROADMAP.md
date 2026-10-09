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
| **A4: Запрет `world.*` в системах** | **ВЫПОЛНЕНО (Шаг P0.5)** | Строгий белый список `world.emit_*` / `world.emit` в телах систем; все остальные методы мира запрещены с подсказкой про `cmd.*`. Уровень 2 доказан тестом `tests/verify_a4_system_world_whitelist.ps1` |
| **A5: Запрет escape-замыканий** | **ВЫПОЛНЕНО (Шаг P0.6)** | Разделение `fn(...)` и `closure(...)`. Запрет `ContainsCapturingClosure()` в сигнатурах функций/методов, полях структур/компонентов/ресурсов, коллекциях (`Vec.push`, `Map.insert`). Уровень 2 доказан тестом `tests/verify_a5_closure_escape_invariant.ps1` |
| **A6: Guards (bounds + liveness)** | **ВЫПОЛНЕНО (Шаг P0.4)** | Проверки `e < 0 \|\| e >= world.entity_count` и `world.entity_arch[e] < 0` добавлены во все `world_set_*`, `world_add_*`, `world_remove_*`, `world_has_*` с вызовом `rt_panic`. Уровень 2 доказан тестом `tests/verify_a6_bounds_invariant.ps1` |
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
- **Статус**: **ВЫПОЛНЕНО** (коммит P0.4; Уровень 2 подтвержден `tests/verify_a6_bounds_invariant.ps1`; Уровень 3 строгий IR-паритет 6/6; Уровень 1 23/23).
- **Шаблон коммита**: `fix(runtime): enforce entity bounds and liveness guards across all world operations`

#### Шаг P0.5 — Белый список вызовов `world.*` внутри систем (A4-fix)
- **Объём**: В `TypeChecker.Declarations.cs` и `TypeChecker.Expressions.cs` при проверке тела системы применить строгий белый список: разрешен ТОЛЬКО `world.emit_*`. Любое обращение к другим методам мира (`spawn`, `despawn`, `set_*`, `add_*`, `remove_*`, `has_*`, `get_*`, `find`, `sort_hierarchy`, `swap_events`, `apply_commands`, `reset_string_arena`) вызывает ошибку компиляции с подсказкой использовать `cmd.*`.
- **Файлы**: `src/ECSLang.Semantics/TypeChecker.Declarations.cs`, `TypeChecker.Expressions.cs`.
- **Критерий приёмки**: Уровень 2 (негативный тест попытки вызова `world.set_*` или `world.has_*` внутри системы падает с диагностической ошибкой компиляции).
- **Статус**: **ВЫПОЛНЕНО** (коммит P0.5; Уровень 2 подтвержден `tests/verify_a4_system_world_whitelist.ps1` (мутации и запросы); Уровень 3 строгий IR-паритет 6/6; Уровень 1 23/23).
- **Шаблон коммита**: `feat(semantics): whitelist world methods inside system bodies to emit_* only`

#### Шаг P0.6 — Запрет escape-замыканий на уровне типов (A5-fix)
- **Объём**: Правило уровня ТИПОВ: тип capturing-closure запрещен в: возвращаемом типе функций (`return`), полях `struct`/`component`/`resource`, типах-аргументах `Vec<T>` / `Map<K, V>`, параметрах пользовательских функций. Разрешен ТОЛЬКО как аргумент встроенных методов коллекций (`for_each`, `map`, `filter`, `any`, `all`, `find`). Замыкания без захвата (чистые функции) разрешены без ограничений.
- **Файлы**: `src/ECSLang.Semantics/TypeChecker.Closures.cs`, `TypeChecker.Statements.cs`, `TypeChecker.Expressions.cs`.
- **Критерий приёмки**: Уровень 2 (негативные тесты возврата и сохранения capturing-closure падают на этапе типизации).
- **Статус**: **ВЫПОЛНЕНО** (коммит P0.6; Уровень 2 подтвержден `tests/verify_a5_closure_escape_invariant.ps1` (4 негативных сценария + 1 позитивный с исполнением); Уровень 3 строгий IR-паритет 6/6; Уровень 1 29/29 built, 23/23 runned exit code 0).
- **Шаблон коммита**: `feat(semantics): prohibit escaping capturing-closure types across signatures and collections`

---

### Блок A7: Снятие предела 64 компонентов (Многословные маски)

#### Шаг A7.2 — Расчет целевой маски через `wordIdx` / `bitIdx` в `set_`, `add_`, `remove_`, `has_`
- **Объём**: Замена хардкода слова 0. Реализация `wordIdx = compId >> 6`, `bitIdx = compId & 63`. Пословное копирование маски, вычисление целевого слова через `OR` / `AND-NOT`, fast-path проверка наличия компонента через целевое слово.
- **Файлы**: `src/ECSLang.Codegen.LLVM/EcsRuntimeEmitter.Archetypes.cs`.
- **Критерий приёмки**: Уровень 3 (рефактор-режим `verify_golden_ir.ps1`, ребейзлайн) + Уровень 1 (`run_all_examples.ps1`).
- **Статус**: **ВЫПОЛНЕНО** (коммит A7.2; Уровень 3 refactor mode 6/6 + strict mode 6/6 после ребейзлайна; Уровень 1 29/29 built, 23/23 runned exit code 0; гистограмма диффов: 0 неклассифицированных строк).
- **Шаблон коммита**: `feat(codegen): implement multi-word target mask calculation in world_set, add, remove, has (Step 2)`

#### Шаг A7.3 — Многословные маски в `despawn`, `grow_archetype`, `spawn_with` и глобальный `@_ecs_zero_mask`
- **Объём**: Внедрение глобальной константы `@_ecs_zero_mask = internal constant [WORDS x i64] zeroinitializer` вместо per-call `alloca` в `ecs_create_world` и `world_assign_a0`. Обновление `spawn_with` (`LlvmCodeGenerator.BulkSpawn.cs`), `despawn` swap-copy и `world_grow_archetype` на пословную адресацию.
- **Файлы**: `src/ECSLang.Codegen.LLVM/EcsRuntimeEmitter.cs`, `EcsRuntimeEmitter.Archetypes.cs`, `LlvmCodeGenerator.BulkSpawn.cs`.
- **Критерий приёмки**: Уровень 3 (дифф-гистограмма + strict match после ребейзлайна) + Уровень 1 (`particles_100k`).
- **Статус**: **ВЫПОЛНЕНО** (коммит A7.3; Уровень 3 refactor mode 6/6 + strict mode 6/6 после ребейзлайна; Уровень 1 29/29 built, 23/23 runned exit code 0; perf particles_100k Release 7.80 ms spawn / 29.04 ms tick).
- **Шаблон коммита**: `feat(codegen): migrate despawn, bulk spawn and runtime zero mask to multi-word arrays (Step 3)`

#### Шаг A7.4 — Многословная фильтрация запросов систем (`query`)
- **Объём**: Представление `allMask`, `anyMask`, `noneMask` как `[WORDS x i64]`. Пословное сопоставление в цикле фильтрации архетипов с пропуском нулевых слов (`zero-word skip`). Миграция строк №20–21 (`world_sort_hierarchy`) на пословную адресацию.
- **Файлы**: `src/ECSLang.Codegen.LLVM/LlvmCodeGenerator.Ecs.cs`, `EcsRuntimeEmitter.Archetypes.cs`.
- **Критерий приёмки**: Уровень 3 (сохранение корректности всех 6 golden IR) + Уровень 1 (`11_ecs_archetypes_and_filters.ecs`).
- **Статус**: **ВЫПОЛНЕНО** (коммит A7.4; Уровень 3 refactor mode 6/6 + strict mode 6/6 после ребейзлайна; Уровень 1 29/29 built, 23/23 runned exit code 0; perf particles_100k Release min 6.36 / median 6.81 ms spawn, min 24.59 / median 29.61 ms tick; P0.3S параллельный стресс-тест эмита 200 000/200 000 пройден 3/3; инварианты A1 и A5 расширены).
- **Шаблон коммита**: `feat(codegen): implement multi-word query filtering with zero-word skip (Step 4)`

---

### 🛑 КПП-1: КАНАРЕЙКА (Валидация многословности > 64 компонентов) — [ПРОЙДЕНО / ГОТОВО К ПРИЁМКЕ]
- **Действие**: Временное повышение порога `TypeChecker` до 128. Создание `tests/canary_70_components.ecs` (72 компонента: `ChildOf` + `C01`..`C71`, $WORDS = 2$, переходы между компонентами с `id < 64` и `id >= 64`, фильтрация `query` с `without`, `spawn_with`, `despawn` swap-copy, иерархия `ChildOf` + `sort_hierarchy` на сущностях с компонентами $id \ge 64$).
- **Критерий приёмки**: Уровень 1 (компиляция в Release и исполнение с кодом возврата 0 и валидацией всех инвариантов).
- **Статус**: ВЫПОЛНЕНО (тест `canary_70_components.ecs` завершился с кодом 0; выявлены и устранены векторы обращения к `curMask` в `world_set` и `world_remove`).
- **Остановка**: Доклад Архитектору результатов канарейки. Ожидание команды «продолжай».

---

### 🛑 КПП-2: Снятие лимита компонентов (Архитектурное решение)

#### Шаг A7.5 — Постоянное снятие предела 64 компонентов
- **Объём**: Замена лимита 64 в `TypeChecker.cs` на потолок 65 535 компонентов с предупреждением компилятора при > 1024 ("compile time grows quadratically with component count (see B7)"). Перевод `tests/canary_70_components.ecs` в статус 7-го эталона `tests/golden_ir/over_64_components.ll`. Добавление двухуровневого режима в `verify_golden_ir.ps1` (`-Mode fast` и `-Mode strict`).
- **Файлы**: `src/ECSLang.Semantics/TypeChecker.cs`, `src/ECSLang.Codegen.LLVM/EcsRuntimeEmitter.cs`, `src/ECSLang.CLI/Program.cs`, `tests/verify_golden_ir.ps1`, `tests/golden_ir/over_64_components.ll`, `tests/verify_a7_5_thresholds.ps1`, `tests/verify_boundary_components.ps1`.
- **Критерий приёмки**: Уровень 3 (7/7 золотых эталонов в strict mode, 6/6 в fast mode) + Уровень 2 (инвариантный тест порогов 65536/1025; граничные тесты 63, 64, 65, 127, 128, 129 с проверкой значений).
- **Статус**: **ВЫПОЛНЕНО**
- **Шаблон коммита**: `feat(semantics): permanently lift component limit to 65535 and add 7th golden IR (Step 5)`

#### Шаг A7.6 — Обновление профайлера F1
- **Объём**: Вывод многословной битовой маски в оверлее профайлера F1 в шестнадцатеричном пословном формате `[0x... 0x...]` с ограничением `DISPLAY_WORDS = min(WORDS, 8)` и суффиксом `...(+N words)` при усечении. Расчёт ширины HUD-панели и буфера строки (512 байт).
- **Файлы**: `src/ECSLang.Codegen.LLVM/EcsRuntimeEmitter.Profiler.cs`, `tests/verify_golden_ir.ps1`, `tests/golden_ir/*.ll`.
- **Критерий приёмки**: Уровень 1 (`examples/18_arcade_void_defender.ecs`) + Уровень 3 (7/7 золотых эталонов в strict mode, подтверждение многословного рендера `[0x%llX 0x%llX]` в `over_64_components.ll`).
- **Статус**: **ВЫПОЛНЕНО**
- **Шаблон коммита**: `feat(profiler): render multi-word archetype masks in F1 telemetry HUD (Step 6)`

#### Шаг A7.Ф — Финализация серии A7 и итоговый perf-отчёт
- **Объём**: Итоговый замер производительности `particles_100k` до/после всей A7-серии (сравнение с коммитом-предком `ddc29ab` через worktree, прогон `tests/perf_baseline.ps1` под Release CLI). Итоговый аудит соответствия `ARCHITECTURE.md` (удаление временного инварианта word-0 переходного периода, фиксация постоянного контракта и капа профайлера). Замер масштабирования фазы кодегена для B7.
- **Файлы**: `ARCHITECTURE.md`, `docs/ROADMAP.md`, `tests/perf_baseline.ps1`, `tests/perf_baseline.csv`.
- **Критерий приёмки**: Уровень 4 (perf-паритет до/после доказан в пределах аппаратного шума; 7/7 golden strict; 29/23 examples; кривая масштабирования для B7 зафиксирована).
- **Статус**: **ВЫПОЛНЕНО**
- **Шаблон коммита**: `test(perf): finalize A7 series performance comparison against pre-A7 baseline`

---

### Блок B: Оптимизации ядра и платформенная переносимость

#### Шаг B7 — Линеаризация кодегена инициализации компонентов
- **Объём**: Устранение квадратичного взрыва времени компиляции и расхода оперативной памяти при большом числе компонентов ($N > 1024$). Фактическая OOM-граница текущей архитектуры: 540 856 строк IR на 72 компонента; квадратичная экстраполяция $(1025/72)^2 \times 540\text{K} \approx 110\text{M}$ строк LLVM IR $\to$ неминуемый Out-Of-Memory (> 16 GB RAM). Замена индивидуальной кодогенерации $O(N^2)$ функций манипуляции компонентами (`set_`, `add_`, `remove_`, `has_`) на компактные табличные дескрипторы компонентов и единый цикл/табличный диспетчер рантайма. Попутно: проверка и выставление `LLVMSetAlignment(8)` для SoA-загрузок, если выявлен дефолт `align 4`. Обновление текста предупреждения в TypeChecker после успешного снятия стены. Калиброванная длительность strict-режима: 223.8 с (из них ~180 с — сравнение 540K-строчного эталона). Тяжёлые компиляции (тест 1025 компонентов) гонять на CI — RAM раннера больше локальной машины.
- **Файлы**: `src/ECSLang.Codegen.LLVM/EcsRuntimeEmitter.cs`, `src/ECSLang.Codegen.LLVM/EcsRuntimeEmitter.Archetypes.cs`, `src/ECSLang.Semantics/TypeChecker.cs`.
- **Критерий приёмки**: Уровень 3 (ребейзлайн всех 7 эталонов Golden IR по двухфазному протоколу с предварительной инвентаризацией) + цели калибровки: время кодегена `canary_70_components` без регрессии против калибровочного числа (~12.8 с при -O0 / ~195 с при -O3) + $N = 1025$ компонентов $< 60$ с + потребление памяти компилятором $< 2$ ГБ (без OOM).
- **Шаблон коммита**: `perf(codegen): linearize component registration codegen via descriptor tables (B7)`

#### Шаг B6 — Флаг компилятора `--pause-on-exit`
- **Объём**: Исключение безусловного авто-вызова `@getchar()` перед выходом из `main`. Генерация ожидания нажатия клавиши строго при наличии CLI-флага `--pause-on-exit`.
- **Файлы**: `src/ECSLang.Codegen.LLVM/LlvmCodeGenerator.cs`, `src/ECSLang.CLI/Program.cs`.
- **Критерий приёмки**: Уровень 1 (чистый headless запуск без зависаний).
- **Шаблон коммита**: `feat(cli): gate console pause-on-exit behind explicit compiler flag`

#### Шаг B3 — Настраиваемое форматирование `f32` / `f64`
- **Объём**: Замена жесткого хардкода `%.2f` в интерполяции строк и `println` на настраиваемое форматирование с дефолтом `%g` / `%f`.
- **Файлы**: `src/ECSLang.Codegen.LLVM/LlvmCodeGenerator.Expressions.cs`.
- **Критерий приёмки**: Уровень 1 (`02_variables_and_math.ecs`).
- **Шаблон коммита**: `feat(codegen): configurable floating point formatting in string interpolation`

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

#### Шаг B5 — POSIX-параллелизм и Linux-контур верификации
Разделён на фазы; каждая — отдельный коммит и приёмка. Первичный контур — GitHub Actions (WSL исключён из-за лимитов RAM локальной машины).

##### Фаза B5.0 — POSIX-target golden IR (без Linux-железа)
- **Объём**: 3 программы (`08_ecs_basics`, `09_ecs_command_buffer`, `13_multithreading_benchmark`) компилируются с `--target x86_64-unknown-linux-gnu --emit-llvm` $\to$ эталоны `tests/golden_ir_posix/`. Верификация: strict/refactor-режимы аналогично Windows-сету. Систематизирует ad-hoc проверку `verify_b1_pause`: регрессии POSIX-пути (`pause`, `getchar`, будущий пул) ловятся на Ур. 3.
- **Шаблон коммита**: `test(posix): establish POSIX-target golden IR baselines`

##### Фаза B5.1 — Рантайм параллелизма (эмиссия как IR)
- **Объём**:
  - `B5.1a` (MVP): parallel-батч = `pthread_create` $N$ + `pthread_join` — просто, корректно; временно допустимая цена $\sim 20$ мкс/поток.
  - `B5.1b`: постоянный пул, эмитится в модуль: `pthread_mutex` + `pthread_cond` + очередь job-функций + счётчик pending; инициализация по guard-флагу перед первым parallel-блоком; барьер = cond-broadcast + ожидание `pending == 0`.
  - Сигнатура `job_{System}` адаптируется под POSIX; batch-of-one $\to$ прямой вызов (переносится бесплатно). Внешние объявления `pthread_*` — как CRT; `ClangGccLinker` уже передаёт `-lpthread -lm -ldl -lrt` — линк-флаги готовы.
- **Критерий приёмки**: Ур. 2 — инвариант «результат parallel == результат последовательного выполнения» (тест-программа со сверкой значений) + Ур. 4 — стресс по образцу P0.3S.
- **Шаблон коммита**: `feat(runtime): pthread-based parallel execution (MVP)` / `feat(runtime): persistent pthread worker pool`

##### Фаза B5.2 — Linux-контур «линк + запуск» (GitHub Actions)
- **Объём**: Workflow `.github/workflows/ecs-linux.yml` (`ubuntu-latest`, push + ночной):
  - `dotnet build` решения; NuGet `libLLVM.runtime.linux-x64` в `ECSLang.Codegen.LLVM` (рядом с `win-x64`);
  - Golden с Linux-хоста: Windows-таргет strict 7/7 — это доказательство **КРОСС-ХОСТОВОГО ДЕТЕРМИНИЗМА** (байт-идентичный IR с разных ОС) + posix-эталоны B5.0;
  - Нативная линковка и запуск консольных examples на раннере (системный clang, `ClangGccLinker`) — Ур. 1; GUI/Network — задокументированные пропуски;
  - Инвариантные тесты `verify_*` через pwsh.
  - **Технические требования**: pwsh-совместимость `.ps1` (пути, командлеты); golden на CI **БЕЗ** `-g` (иначе DIFile-пути ломают идентичность); EOL эталонов зафиксировать как LF; perf-замеры на CI **НЕ** проводить (shared-раннеры шумные); бейдж в README — только после КПП-3, вместе с C1.
- **Критерий приёмки**: зелёный workflow (пункты 2–4) + запуск `13_multithreading_benchmark` с нативной линковкой на раннере.
- **Шаблон коммита**: `ci(linux): establish ubuntu verification workflow`
- **Скоуп-ограничения**: Raylib-примеры вне B5 (консольных достаточно); кросс-линк lld + musl из Windows — отдельный будущий пункт, не B5; ручная глубокая проверка перед релизами — Live USB.

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
