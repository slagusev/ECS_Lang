# Справочник по языку ECSLang (Language Reference)

Официальная справка по синтаксису, типам данных, встроенным конструкциям и архитектурным элементам языка программирования **ECSLang**.

Файлы исходного кода имеют расширение `.ecs`.

---

## Содержание
1. [Базовая структура программы](#1-базовая-структура-программы)
2. [Система типов](#2-система-типов)
3. [Переменные, константы и мутабельность](#3-переменные-константы-и-мутабельность)
4. [Операторы и выражения](#4-операторы-и-выражения)
5. [Управляющие конструкции](#5-управляющие-конструкции)
6. [Пользовательские функции](#6-пользовательские-функции)
7. [Пользовательские структуры данных](#7-пользовательские-структуры-данных)
8. [Массивы фиксированного размера](#8-массивы-фиксированного-размера)
9. [Перечисления и сопоставление с образцом](#9-перечисления-и-сопоставление-с-образцом)
10. [Элементы ECS (Entity Component System)](#10-элементы-ecs-entity-component-system)
   - [Компоненты (component)](#компоненты-component)
   - [Ресурсы (resource)](#ресурсы-resource)
   - [Изолированные контексты миров (World)](#изолированные-контексты-миров-world)
   - [Манипуляция сущностями (spawn, set, add, remove, has)](#манипуляция-сущностями)
   - [Системы (system) и запросы (query)](#системы-system-и-запросы-query)
   - [Конвейеры выполнения (pipeline)](#конвейеры-выполнения-pipeline)
   - [Иерархия сущностей (ChildOf, sort_hierarchy)](#иерархия-сущностей)
   - [События (event) и наблюдатели (read)](#события-event-и-наблюдатели-read)
11. [Встроенные функции ввода-вывода](#11-встроенные-функции-ввода-вывода)
12. [Графика, окно, мультимедиа и ввод (Raylib)](#12-графика-окно-мультимедиа-и-ввод-raylib)
    - [Управление окном и временем](#управление-окном-и-временем)
    - [Отрисовка 2D примитивов](#отрисовка-2d-примитивов)
    - [2D Камера (Camera2D)](#2d-камера-camera2d)
    - [Текстуры и спрайты](#текстуры-и-спрайты)
    - [Аудиосистема и звуки](#аудиосистема-и-звуки)
    - [Обработка ввода (Клавиатура и мышь)](#обработка-ввода-клавиатура-и-мышь)
    - [Работа с цветами](#работа-с-цветами)
    - [Пример игрового цикла](#пример-игрового-цикла)
13. [Буфер команд и мутация мира (Command Buffer)](#13-буфер-команд-и-мутация-мира-command-buffer--deferred-mutations)
14. [Справочник по CLI компилятора и отладке](#14-справочник-по-cli-компилятора-и-отладке)
15. [Модульность и многофайловые проекты (import)](#15-модульность-и-многофайловые-проекты-import)
16. [Строковые операции, интерполяция и математика](#16-строковые-операции-интерполяция-и-математика)
17. [Хэш-таблицы (HashMap<K, V>) и именованная индексация сущностей](#17-хэш-таблицы-hashmapk-v-и-именованная-индексация-сущностей)
18. [Аренное управление памятью строк (World String Arena)](#18-аренное-управление-памятью-строк-world-string-arena)
19. [Безопасные типы Option<T> и Result<T, E> с сопоставлением с образцом (Pattern Matching)](#19-безопасные-типы-optiont-и-resultt-e-с-сопоставлением-с-образцом-pattern-matching)
20. [Стандартная библиотека GUI компонентов (Pure ECSLang GUI Standard Library)](#20-стандартная-библиотека-gui-компонентов-pure-ecslang-gui-standard-library)
21. [Замыкания, лямбда-выражения и методы высшего порядка (Closures & Lambdas)](#21-замыкания-лямбда-выражения-и-методы-высшего-порядка-closures--lambdas)

---

## 1. Базовая структура программы

Точкой входа в программу является функция `main`, возвращающая целочисленный код возврата (`i32`):

```rust
fn main(): i32 {
    println("Hello, ECSLang!");
    return 0;
}
```

---

## 2. Система типов

### Примитивные типы:
| Тип | Размер | Описание | Пример |
|---|---|---|---|
| `i32` | 32 бита | Знаковое целое число (по умолчанию для целых) | `42`, `-10` |
| `i64` | 64 бита | Знаковое 64-битное целое число | `100000` |
| `f32` | 32 бита | Вещественное число с плавающей точкой (одинарная точность) | `3.14`, `-0.5` |
| `f64` | 64 бита | Вещественное число с плавающей точкой (двойная точность) | `3.1415926535` |
| `bool` | 1 бит | Логический тип | `true`, `false` |
| `string` | Указатель | Строковый литерал в кодировке UTF-8 | `"Привет, мир!"` |
| `void` | 0 бит | Отсутствие возвращаемого значения у функции | `fn log(): void` |
| `World` | Указатель | Изолированный контекст мира ECS | `ecs::create_world()` |

### Составные и пользовательские типы:
| Тип | Описание | Пример |
|---|---|---|
| `[T; N]` | Массив фиксированного размера `N` из элементов типа `T` | `[i32; 5]`, `[f32; 4]` |
| `[T]` / `Vec<T>` | Динамический массив переменной длины с авто-реаллокацией | `Vec<i32>()`, `[f32] = []` |
| `enum Name` | Перечисление с 32-битными целочисленными значениями | `enum State { Idle, Running }` |
| `struct Name` | Пользовательская структура данных | `struct Vector2 { x: f32, y: f32 }` |

---

## 3. Переменные, константы и мутабельность

### Переменные (`let` и `let mut`)

Объявление локальных переменных выполняется с помощью ключевого слова `let`. По умолчанию переменные иммутабельны. Для разрешения изменения значения используется ключевое слово `mut`:

```rust
// Иммутабельные переменные
let x = 10;
let message = "Строка";
let speed: f32 = 9.81; // Явное указание типа

// Мутабельные переменные
let mut counter = 0;
counter = counter + 1;
counter += 5;
```

### Compile-Time Константы (`const`)

Для объявления именованных констант времени компиляции используется ключевое слово `const`. Константы вычисляются компилятором на этапе семантического анализа (Pass 0.5) и **вжигаются** непосредственно в LLVM IR как немедленные операнды (Zero-Cost Abstraction) без выделения памяти на стеке или в секции данных:

```rust
// Глобальные и локальные константы
const MAX_PLAYERS: i32 = 100;
const GRAVITY: f32 = 9.81;
const PI: f64 = 3.1415926535;
const APP_TITLE: string = "ECS Game Engine";
const DEBUG_MODE: bool = true;

// Выражения из констант вычисляются на этапе компиляции (Constant Folding)
const BUFFER_SIZE: i32 = 1024 * 4;
const OFFSET_X: f32 = 10.0 + 5.0 * 2.0;

fn main(): i32 {
    let limit = MAX_PLAYERS;
    println($"Limit: {limit}, Buffer: {BUFFER_SIZE}");
    return 0;
}
```

Константы могут использоваться:
- В выражениях любого уровня вложенности;
- В качестве значений в ветвях сопоставления с образцом `match`;
- В стандартных библиотеках кодов клавиш и кнопок мыши (`std/keys.ecs`, `std/mouse.ecs`).

Попытка присвоить новое значение константе (`MAX_PLAYERS = 200;`) пресекается компилятором на этапе семантического анализа с ошибкой `CannotAssignToConst`.

---

## 4. Операторы и выражения

- **Арифметические**: `+`, `-`, `*`, `/`, `%`
- **Операторы составного присваивания**: `=`, `+=`, `-=`, `*=`, `/=`
- **Операторы сравнения**: `==`, `!=`, `<`, `<=`, `>`, `>=` (возвращают `bool`)
- **Логические операторы**: `&&` (И), `||` (ИЛИ), `!` (НЕ)
- **Оператор диапазона**: `start..end` (полуинтервал $[start, end)$)
- **Оператор разрешения модуля**: `::` (например, `ecs::create_world()`)
- **Оператор доступа к полям и методам**: `.` (например, `pos.x`, `world.spawn()`)
- **Оператор явного приведения типов**: `expr as Type`

### Оператор приведения типов (`as`)

Оператор `as` выполняет явное, безопасное приведение числовых типов. В сгенерированном LLVM IR он разворачивается в прямые машинные инструкции процессора без накладных расходов:

```rust
let integer_val: i32 = 42;
let float_val: f32 = integer_val as f32; // SIToFP: i32 -> f32
let double_val: f64 = float_val as f64;  // FPExt: f32 -> f64
let int64_val: i64 = integer_val as i64; // SExt: i32 -> i64

let truncated: i32 = 99.85 as i32;       // FPToSI: f64 -> i32 (получит 99)
let truncated_int: i32 = int64_val as i32; // Trunc: i64 -> i32
```

Поддерживаемые направления приведений:
- `i32` $\leftrightarrow$ `f32`, `f64`, `i64`
- `i64` $\leftrightarrow$ `f32`, `f64`, `i32`
- `f32` $\leftrightarrow$ `f64`, `i32`, `i64`
- `f64` $\leftrightarrow$ `f32`, `i32`, `i64`
- Тождественные приведения (`T as T`) сводятся компилятором к no-op.

---

## 5. Управляющие конструкции

### Условный оператор `if / else`:
```rust
let health = 75;

if health > 50 {
    println("Состояние отличное");
} else if health > 20 {
    println("Требуется лечение");
} else {
    println("Критическое состояние");
}
```

### Цикл `while`:
```rust
let mut counter = 3;
while counter > 0 {
    print("Отсчет: ");
    println(counter);
    counter -= 1;
}
```

### Диапазонный цикл `for`:
Цикл `for` перебирает значения от `start` до `end` (не включая `end`):
```rust
let mut total = 0;
for i in 0..5 {
    // i принимает значения 0, 1, 2, 3, 4
    total += i;
}
println(total); // 10
```

### Управление циклами: `break` и `continue`

ECSLang поддерживает операторы досрочного прерывания `break` и перехода на следующую итерацию `continue` внутри циклов `while` и `for`. Компилятор проверяет контекст выполнения на этапе семантического анализа и запрещает вызов `break` или `continue` вне тела цикла (`CannotBreakOutsideLoop` / `CannotContinueOutsideLoop`).

#### Использование `break`:
Оператор `break` немедленно завершает выполнение ближайшего объемлющего цикла, передавая управление на базовый блок выхода (`exitBB`):

```rust
let mut sum = 0;
for i in 0..100 {
    if i == 5 {
        break; // Прерываем цикл, когда i достигает 5
    }
    sum += i;
}
println($"Sum: {sum}"); // 0 + 1 + 2 + 3 + 4 = 10
```

#### Использование `continue`:
Оператор `continue` немедленно завершает текущую итерацию:
- В цикле `while`: передает управление на проверку условия (`condBB`);
- В цикле `for i in start..end`: передает управление на блок инкремента счетчика (`forIncBB`), выполняя шаг переменной `i++` и предотвращая зацикливание, после чего проверяет условие выхода.

```rust
let mut odd_count = 0;
for i in 0..10 {
    // Пропускаем четные числа
    if i % 2 == 0 {
        continue;
    }
    odd_count += 1;
}
println($"Odd count: {odd_count}"); // 5 (1, 3, 5, 7, 9)
```

---

## 6. Пользовательские функции

Функции объявляются ключевым словом `fn`. Функции можно вызывать в любом месте программы независимо от порядка их декларации (двухпроходное связывание):

```rust
// Функция с параметрами и возвращаемым значением
fn multiply(a: i32, b: i32): i32 {
    return a * b;
}

// Функция без возвращаемого значения (void)
fn log_status(code: i32): void {
    print("Код статуса: ");
    println(code);
}

// Использование в main
fn main(): i32 {
    let result = multiply(6, 7);
    log_status(result);
    return 0;
}
```

---

## 7. Пользовательские структуры данных

Структуры данных (`struct`) объединяют типизированные поля. Доступ и мутация полей осуществляются через оператор точку `.`:

```rust
struct Vector2 {
    x: f32,
    y: f32,
}

struct PlayerInfo {
    id: i32,
    score: i32,
}

fn add_vectors(a: Vector2, b: Vector2): Vector2 {
    return Vector2(a.x + b.x, a.y + b.y);
}

fn main(): i32 {
    // Вызов конструктора
    let v1 = Vector2(10.0, 20.0);
    let v2 = Vector2(5.5, 4.5);
    
    // Передача структуры в функцию
    let mut v3 = add_vectors(v1, v2);
    
    // Чтение и модификация полей
    v3.x += 100.0;
    println(v3.x); // 115.5
    println(v3.y); // 24.5
    return 0;
}
```

### Методы структур и компонентов (`impl`)

Блок `impl Name { ... }` позволяет связывать методы и статические функции непосредственно с пользовательскими структурами (`struct`) и компонентами (`component`):

```rust
struct Vector2 {
    x: f32,
    y: f32,
}

impl Vector2 {
    // Статический / ассоциированный метод (без self)
    fn zero(): Vector2 {
        return Vector2(0.0, 0.0);
    }

    fn create(x: f32, y: f32): Vector2 {
        return Vector2(x, y);
    }

    // Метод чтения (self передается по неявному указателю, zero-copy)
    fn length_sq(self): f32 {
        return self.x * self.x + self.y * self.y;
    }

    fn dot(self, other: Vector2): f32 {
        return self.x * other.x + self.y * other.y;
    }

    // Мутирующий метод (mut self изменяет поля целевой структуры in-place)
    fn scale(mut self, factor: f32): void {
        self.x *= factor;
        self.y *= factor;
    }

    fn add_in_place(mut self, other: Vector2): void {
        self.x += other.x;
        self.y += other.y;
    }
}
```

#### Вызов методов:
- **Ассоциированные функции**: `let mut v = Vector2::create(3.0, 4.0);`
- **Методы экземпляра**: `let len = v.length_sq();`
- **Мутирующие вызовы**: `v.scale(2.0);` (изменяет `v` на месте без перевыделения памяти)
- **Методы на компонентах в системах**:
  ```rust
  component Position { x: f32, y: f32 }
  impl Position {
      fn translate(mut self, dx: f32, dy: f32): void {
          self.x += dx;
          self.y += dy;
      }
  }

  system MoveSystem {
      query(mut pos: Position) {
          pos.translate(1.0, 0.5); // Прямая мутация в чанке архетипа!
      }
  }
  ```

---

## 8. Массивы фиксированного размера `[T; N]`

Массивы фиксированного размера хранят строго заданное количество элементов одного типа `T` в непрерывной памяти:

### Декларация и литералы:
```rust
// Массив с явным типом
let mut numbers: [i32; 5] = [10, 20, 30, 40, 50];

// Вывод типа по литералу
let coords = [1.5, 2.5, 3.5]; // [f32; 3]
```

### Индексация (чтение и запись):
Индексация начинается с `0`:
```rust
let val = numbers[2];    // 30
numbers[2] = 99;         // Запись по индексу
numbers[2] += 1;         // Составное присваивание (100)
```

### Итерация через цикл `for`:
```rust
for i in 0..5 {
    println("numbers[%d] = %d", i, numbers[i]);
}
```

### Массивы в компонентах и структурах:
```rust
component Inventory {
    items: [i32; 4]
}

system UpdateItems {
    query(mut inv: Inventory) {
        for i in 0..4 {
            inv.items[i] += 10;
        }
    }
}
```

---

## 8.1. Динамические массивы (`Vec<T>` / `List<T>` / `[T]`)

Динамические массивы представляют собой автоматически масштабируемые непрерывные буферы памяти с zero-overhead структурой `{ T* data, i32 length, i32 capacity }` (16 байт на x64).

### Способы создания:
```rust
// Через обобщенный конструктор Vec<T>()
let mut numbers = Vec<i32>();

// Через синтаксис среза [T] с пустым литералом []
let mut floats: [f32] = [];

// С начальной инициализацией элементами
let mut scores: [i32] = [100, 200, 300];
```

### Основные методы:
- `.push(item)`: добавляет элемент в конец массива. При нехватке места емкость экспоненциально удваивается (4, 8, 16...) через `realloc`.
- `.pop()`: удаляет и возвращает последний элемент массива, уменьшая длину на 1.
- `.len()` или `.length()`: возвращает текущее количество элементов (`i32`, $O(1)$).
- `.capacity()`: возвращает текущую выделенную емкость буфера (`i32`, $O(1)$).
- `.clear()`: сбрасывает длину в 0 без освобождения выделенной памяти (для эффективного повторного наполнения без реаллокаций).

### Индексация и итерация `for-in`:
```rust
let mut list = Vec<i32>();
list.push(10);
list.push(20);

// Чтение и запись по индексу:
list[0] = 15;
let first = list[0]; // 15

// Прямой обход элементов через for-in:
for item in list {
    println("Значение: %d", item);
}
```

---

## 9. Перечисления (`enum`) и сопоставление с образцом (`match`)

### Перечисления (`enum`):
Определяют набор именованных констант с 32-битным представлением (`i32`). Значения могут нумероваться автоматически с `0` или задаваться явно:

```rust
enum PlayerState {
    Idle,            // 0
    Walking,         // 1
    Running,         // 2
    Attacking = 10,  // 10
    Dead             // 11
}

// Использование в качестве типа переменной
let mut state: PlayerState = PlayerState.Idle;
state = PlayerState.Attacking;

// Использование в компонентах
component Character {
    state: PlayerState,
    health: i32
}
```

### Сопоставление с образцом (`match`):
Оператор `match` проверяет значение выражения против шаблонов и выполняет соответствующий блок. Компилируется в эффективную таблицу переходов LLVM:

```rust
match state {
    PlayerState.Idle => {
        println("Персонаж бездействует");
    }
    PlayerState.Walking => {
        println("Персонаж идет");
    }
    PlayerState.Attacking => {
        println("Персонаж атакует!");
    }
    _ => {
        // Ветка по умолчанию для всех остальных состояний
        println("Другое состояние");
    }
}
```

Также поддерживается сопоставление по целочисленным константам:
```rust
let score = 100;
match score {
    100 => {
        println("Максимальный балл!");
    }
    50 => {
        println("Средний балл");
    }
    _ => {
        println("Другой результат: %d", score);
    }
}
```

---

## 10. Элементы ECS (Entity Component System)

### Компоненты (`component`)
Описывают чистые данные сущностей. Хранятся в непрерывных массивах архетипов (SoA):
```rust
component Position {
    x: f32,
    y: f32,
}

component Velocity {
    vx: f32,
    vy: f32,
}

component PlayerTag {
    id: i32,
}
```

### Ресурсы (`resource`)
Глобальные для конкретного мира синглтон-данные (например, время кадра, настройки):
```rust
resource Time {
    dt: f32,
}

resource GameConfig {
    gravity: f32,
    debug_mode: bool,
}
```

### Изолированные контексты миров (`World`)
Миры изолированы друг от друга и создаются вызовом `ecs::create_world()`:
```rust
let mut game_world = ecs::create_world();
let mut ui_world = ecs::create_world();

// Установка значений ресурсов для конкретного мира
game_world.set_Time(0.016);
game_world.set_GameConfig(9.81, true);

// Прямое чтение ресурса из мира:
let cfg = game_world.get_GameConfig();
println($"Gravity: {cfg.gravity}");

// Цепочечный доступ к полям ресурса:
if game_world.get_GameConfig().debug_mode {
    println("Debug mode enabled");
}
```

### Манипуляция сущностями
```rust
// 1. Создание сущности
let e = world.spawn();

// 2. Инициализация компонентов при создании
world.set_Position(e, 100.0, 200.0);
world.set_Velocity(e, 2.0, -1.0);

// 3. Динамическая проверка наличия компонента
if world.has_Velocity(e) {
    println("Сущность подвижна");
}

// 4. Динамическое удаление компонента (миграция архетипа)
world.remove_Velocity(e);

// 5. Динамическое добавление компонента (миграция архетипа)
world.add_Velocity(e, 5.0, 0.0);
```

### Системы (`system`) и запросы (`query`)
Системы содержат логику обработки данных. Компилятор гарантирует валидацию мутабельного доступа (`mut`):
```rust
system MovementSystem {
    query(mut pos: Position, vel: Velocity, time: Time) {
        pos.x += vel.vx * time.dt;
        pos.y += vel.vy * time.dt;
    }
}

system RenderSystem {
    query(pos: Position) {
        // Доступ только для чтения
        print("X: ");
        println(pos.x);
    }
}

#### Фильтры запросов (`without` и `with`)
Системы поддерживают предикаты фильтрации архетипов на уровне компилятора без накладных расходов во внутреннем цикле:
- **`without: Component` (Исключение)**: система обрабатывает только те сущности, у которых **отсутствует** указанный компонент:
  ```rust
  // Двигать только незамороженных и живых сущностей
  system MovementSystem {
      query(mut pos: Position, vel: Velocity, without: Frozen, without: Dead) {
          pos.x += vel.vx;
          pos.y += vel.vy;
      }
  }
  ```
- **`with: Component` (Маркерные компоненты)**: система требует наличия компонента у сущности, но **не выполняет чтение или биндинг полей** компонента в локальные переменные:
  ```rust
  // Лечить только сущности с компонентом-маркером Player
  system PlayerRegenSystem {
      query(mut h: Health, with: Player) {
          h.hp += 10;
      }
  }
  ```

Фильтры работают на уровне битовых масок архетипов мира: при итерации конвейера отбираются только архетипы `(arch_mask & required == required) && (arch_mask & without == 0)`.

### Конвейеры выполнения (`pipeline`)
Пайплайны организуют вызов систем по стадиям (`stage`). Конвейер принимает конкретный экземпляр мира:
```rust
pipeline GamePipeline {
    stage Update {
        // Параллельное выполнение систем на всех ядрах CPU (Windows ThreadPool)
        parallel {
            MovementSystem;    // mut Position, Velocity
            ShieldRegenSystem; // mut Shield
            HealthRegenSystem; // mut Health
        }
        sync; // Барьер синхронизации
    }
    stage Render {
        RenderSystem;
    }
}

fn main(): i32 {
    let mut world = ecs::create_world();
    // ... наполнение мира ...
    
    // Запуск конвейера над миром
    GamePipeline(world);
    return 0;
}
```

> [!TIP]
> **Автоматический контроль гонок данных (Data-Race Free)**: Компилятор статически анализирует граф зависимостей (DAG). Если две системы в блоке `parallel` попытаются одновременно получить мутабельный доступ к одному компоненту или ресурсу (конфликты `write-read` или `write-write`), компилятор выдаст ошибку на этапе компиляции до генерации бинарного файла. Системы, читающие общие данные (`const`), выполняются параллельно без блокировок.

### Иерархия сущностей
ECSLang поддерживает встроенную иерархию через встроенный компонент `ChildOf`:
```rust
pipeline UiPipeline {
    stage Layout {
        // Автоматическая топологическая сортировка элементов по глубине
        sort_hierarchy;
    }
}

fn main(): i32 {
    let mut ui = ecs::create_world();
    let root = ui.spawn();
    let child = ui.spawn();
    
    // Связывание дочерней сущности с родителем
    ui.set_ChildOf(child, root);
    
    UiPipeline(ui);
    return 0;
}
```

### События (`event`) и наблюдатели (`read`)

ECSLang предоставляет реактивную модель событий с нулевой стоимостью копирования очереди (Double-Buffered Event Queues).

#### 1. Декларация событий
```rust
event Collision {
    entity_a: i32,
    entity_b: i32,
    force: f32,
}

event GameOver {
    winner_id: i32,
}
```

#### 2. Отправка событий (`world.emit`)
События могут генерироваться как из систем, так и из обычных функций:
```rust
// Способ 1: Прямой вызов метода генерации события
world.emit_Collision(1, 2, 45.5);

// Способ 2: Использование синтаксиса конструктора структуры события
world.emit(Collision(1, 2, 45.5));
world.emit(GameOver(1));
```

#### 3. Системы-наблюдатели (`read`)
Для подписки на события вместо `query(...)` используется конструкция `read(...)`. Система итерирует поступившие события из буфера чтения и может одновременно запрашивать мутабельные или константные ресурсы:
```rust
system DamageObserver {
    read(col: Collision, mut stats: GameStats) {
        stats.total_events += 1;
        println("Коллизия между сущностями %d и %d, сила: %f", col.entity_a, col.entity_b, col.force);
    }
}
```

#### 4. Двойная буферизация и сброс очередей (`swap_events`)
Все события пишутся в буфер записи текущего кадра/фазы. Для перехода событий в буфер чтения выполняется мгновенный $O(1)$ обмен указателями:
- **Автоматически**: в начале каждого вызова конвейера `pipeline`, если в конвейере нет явных указаний `swap_events;`.
- **Явно в конвейере**: ключевым словом `swap_events;` на границе стадий:
```rust
pipeline GamePipeline {
    stage Physics {
        MovementSystem; // Генерирует world.emit_Collision
    }
    stage Reaction {
        swap_events;    // Переносит накопленные события физики в буфер чтения
        DamageObserver; // Обрабатывает Collision
    }
}
```
- **Программно**: вызовом метода `world.swap_events();`.

---

## 11. Встроенные функции ввода-вывода

| Функция | Сигнатура | Описание |
|---|---|---|
| `print(value)` | `(Any): void` | Печать значения (строка, число, bool) в консоль без переноса строки. |
| `println(value)` | `(Any): void` | Печать значения с переносом строки. Без аргументов печатает пустую строку. |
| `wait_key()` | `(): i32` | Ожидание нажатия клавиши пользователем (под капотом CRT `getchar()`). |
| `readln()` | `(): i32` | Считывание символа из стандартного потока ввода. |
| `get_tick_count()` | `(): i32` | Текущее время в миллисекундах от старта системы (`GetTickCount` из `kernel32.dll`) для бенчмарков и таймингов. |
| `time_ms()` | `(): i32` | Псевдоним для `get_tick_count()`. |

> [!NOTE]
> Компилятор автоматически внедряет печать `"Press Enter to exit..."` и вызов `getchar()` перед выходом из `main`, если в коде отсутствует явный вызов `wait_key()`. Это предотвращает преждевременное закрытие консольного окна в Windows.

### 11.2. Промышленный файловый ввод-вывод (Big Data File I/O)

ECSLang включает высокопроизводительные кроссплатформенные функции для работы с файлами любого объема (включая терабайтные логи и бинарные дампы) через стандартный C ABI (CRT). Все операции ввода-вывода обладают следующими архитектурными гарантиями:
- **64-битная адресация смещений**: на Windows используются нативные `_fseeki64` и `_ftelli64` из `ucrt.lib`, на Linux/macOS — `fseeko` и `ftello` (POSIX 64-bit offset). Файлы размером > 2 ГБ не вызывают переполнения.
- **Атомарность записи (Thread-Safe File Locking)**: при записи и дозаписи дескриптор файла блокируется на уровне операционной системы (`_lock_file`/`_unlock_file` на Windows, `flockfile`/`funlockfile` на POSIX), что исключает перемешивание байт и строк при параллельной записи из нескольких систем ECS.
- **Интеграция с `Result<T, string>` и оператором `?`**: безопасная обработка ошибок без падений и без сырых Си-кодов возврата.

| Функция | Сигнатура | Возвращает | Описание |
|---|---|---|---|
| `file_exists(path)` | `(string): bool` | `bool` | Быстрая проверка наличия файла на диске. |
| `file_read_text(path)` | `(string): Result<string, string>` | `Result<string, string>` | Читает текстовый файл целиком в память с блокировкой и 64-битным смещением. При успехе возвращает `Ok(content)`. |
| `file_write_text(path, content)` | `(string, string): Result<bool, string>` | `Result<bool, string>` | Атомарно записывает/перезаписывает текстовый файл под эксклюзивным локом. При успехе возвращает `Ok(true)`. |
| `file_append_text(path, line)` | `(string, string): Result<bool, string>` | `Result<bool, string>` | Атомарно дописывает строку в конец файла (режим `"ab"` под локом). Идеально для параллельного Big Data логирования. |
| `file_read_bin(path)` | `(string): Result<Vec<u8>, string>` | `Result<Vec<u8>, string>` | Открывает файл в бинарном режиме (`"rb"`), считывает сырые байты в динамический массив `Vec<u8>`. |
| `file_write_bin(path, bytes)` | `(string, Vec<u8>): Result<bool, string>` | `Result<bool, string>` | Атомарно записывает сырой массив байт `Vec<u8>` на диск в бинарном режиме (`"wb"`). |

#### Пример текстового I/O с оператором `?`:
```rust
fn save_game_state(path: string, json: string): Result<bool, string> {
    file_write_text(path, json)?;
    file_append_text("game.log", "[LOG] Game state saved successfully\n")?;
    return Ok(true);
}
```

#### Пример бинарного Big Data I/O (`Vec<u8>`):
```rust
fn dump_binary_payload(path: string): Result<bool, string> {
    let mut payload = Vec<u8>();
    payload.push(69 as u8);  // 'E'
    payload.push(67 as u8);  // 'C'
    payload.push(83 as u8);  // 'S'
    payload.push(0 as u8);   // Бинарный ноль
    file_write_bin(path, payload)?;

    let bytes = file_read_bin(path)?;
    println($"Считано {bytes.len()} байт. Первый байт: {bytes[0]}");
    return Ok(true);
}
```

### 11.3. Строковые операции Tier 1

ECSLang предоставляет высокоэффективные встроенные методы работы со строками через точечную нотацию (`s.method(...)`). На машинном уровне вызовы транслируются напрямую в низкоуровневые функции C ABI CRT (`strlen`, `strstr`, `strncmp`, `strcmp`, `memcpy`) без оверхеда промежуточных объектов-оберток:

| Метод | Сигнатура | Возвращает | Описание |
|---|---|---|---|
| `s.len()` / `s.length()` | `(): i32` | `i32` | Возвращает длину строки в символах (`strlen`). |
| `s.contains(sub)` | `(string): bool` | `bool` | Проверяет наличие подстроки в строке (`strstr != null`). |
| `s.starts_with(prefix)` | `(string): bool` | `bool` | Проверяет, начинается ли строка с указанного префикса (`strncmp == 0`). |
| `s.ends_with(suffix)` | `(string): bool` | `bool` | Проверяет, заканчивается ли строка указанным суффиксом (сравнение смещения указателя через `strcmp`). |
| `s.index_of(sub)` | `(string): i32` | `i32` | Возвращает 0-индексированную позицию первого вхождения подстроки или `-1`, если не найдено. |
| `s.substring(start, len)` | `(i32, i32): string` | `string` | Вырезает подстроку с автоматической защитой от выхода за границы (out-of-bounds safety) и выделением в String Arena. |

#### Пример анализа логов Big Data:
```rust
let log = "2026-10-02 [INFO] GET /api/v1/metrics HTTP/1.1 200";

if log.starts_with("2026-10-02") && log.contains("[INFO]") && log.ends_with("200") {
    let uri_start = log.index_of("/api/");
    let uri = log.substring(uri_start, 15);
    println($"Parsed URI: {uri}");
}
```

---

## 12. Графика, окно и ввод (Raylib)

ECSLang содержит встроенную нативную интеграцию с графической библиотекой **Raylib 6.0** (x64 MSVC).

### Управление окном и временем
| Функция | Сигнатура | Описание |
|---|---|---|
| `init_window(width, height, title)` | `(i32, i32, string): void` | Инициализирует графическое окно и контекст OpenGL. Присутствие этого вызова автоматически отключает консольную задержку `"Press Enter to exit..."`. |
| `window_should_close()` | `(): bool` | Возвращает `true`, если пользователь нажал кнопку закрытия окна или клавишу Esc. |
| `close_window()` | `(): void` | Закрывает графическое окно и освобождает контекст OpenGL. |
| `set_target_fps(fps)` | `(i32): void` | Задает целевую кадровую частоту (например, 60). |
| `get_fps()` | `(): i32` | Возвращает текущую частоту кадров в секунду. |
| `get_frame_time()` | `(): f32` | Возвращает время отрисовки предыдущего кадра в секундах (`delta time`). |
| `get_time()` | `(): f64` | Возвращает общее время работы приложения в секундах. |

### Отрисовка 2D примитивов
Компилятор автоматически приводит вещественные числа (`f32`) компонентов к целым пиксельным координатам при передаче в функции отрисовки.

| Функция | Сигнатура | Описание |
|---|---|---|
| `begin_drawing()` | `(): void` | Открывает буфер отрисовки текущего кадра. |
| `end_drawing()` | `(): void` | Завершает отрисовку кадра и выполняет swapchain (вывод на экран). |
| `clear_background(color)` | `(i32): void` | Очищает экран указанным цветом RGBA. |
| `draw_rectangle(x, y, w, h, color)` | `(i32, i32, i32, i32, i32): void` | Отрисовка заполненного прямоугольника. Координаты автоматически конвертируются из `f32` при необходимости. |
| `draw_circle(cx, cy, radius, color)` | `(i32, i32, f32, i32): void` | Отрисовка заполненного круга. Координаты центра автоматически конвертируются из `f32`. |
| `draw_line(x1, y1, x2, y2, color)` | `(i32, i32, i32, i32, i32): void` | Отрисовка линии между двумя точками. |
| `draw_text(text, x, y, size, color)` | `(string, i32, i32, i32, i32): void` | Отрисовка текста встроенным растровым шрифтом. |

### 2D Камера (Camera2D)
ECSLang поддерживает матричные 2D трансформации мира (скроллинг, зум, вращение камеры) через встроенные вызовы Raylib:

| Функция | Сигнатура | Описание |
|---|---|---|
| `begin_mode_2d(ox, oy, tx, ty, rot, zoom)` | `(f32, f32, f32, f32, f32, f32): void` | Активирует 2D камеру с указанными экранным смещением (`offset`), мировой целью (`target`), углом вращения в градусах (`rotation`) и масштабом (`zoom`). |
| `end_mode_2d()` | `(): void` | Завершает режим 2D камеры и возвращает стандартную экранную матрицу проекции. |

```rust
// Камера центрирована на игроке (player_x, player_y) с масштабом 1.5x:
begin_mode_2d(400.0, 300.0, player_x, player_y, 0.0, 1.5);
// Все последующие draw_* рендерятся в координатах игрового мира!
draw_rectangle(world_box_x, world_box_y, 64, 64, rl_color(200, 50, 50, 255));
end_mode_2d();
```

### Текстуры и спрайты
Поддерживается загрузка графических форматов PNG, BMP, TGA, JPG, вычитка геометрии и аппаратная отрисовка:

| Функция | Сигнатура | Описание |
|---|---|---|
| `load_texture(path)` | `(string): i64` | Загружает текстуру в видеопамять VRAM и возвращает opaque дескриптор текстуры. |
| `unload_texture(tex)` | `(i64): void` | Выгружает текстуру из видеопамяти и освобождает ресурсы. |
| `get_texture_width(tex)` | `(i64): i32` | Возвращает ширину текстуры в пикселях. |
| `get_texture_height(tex)` | `(i64): i32` | Возвращает высоту текстуры в пикселях. |
| `draw_texture(tex, x, y, [color | r, g, b, a])` | `(i64, i32, i32, ...): void` | Отрисовывает текстуру в позиции (x, y) с оттенком tint. |
| `draw_texture_pro(tex, sx, sy, sw, sh, dx, dy, dw, dh, ox, oy, rot, [color | r, g, b, a])` | `(i64, f32..., ...): void` | Профессиональная отрисовка спрайта: исходный прямоугольник `source`, целевой прямоугольник `dest`, точка вращения `origin`, угол `rot` и цветовой фильтр. |

```rust
let player_tex = load_texture("assets/hero.png");
let width = get_texture_width(player_tex);

// Отрисовка кадра спрайтшита:
draw_texture_pro(player_tex,
    frame_x, frame_y, 32.0, 32.0, // source rect
    pos_x, pos_y, 64.0, 64.0,     // dest rect (масштаб 2x)
    32.0, 32.0,                   // origin (центр спрайта)
    rotation,                     // угол поворота
    255, 255, 255, 255            // RGBA оттенок
);

unload_texture(player_tex);
```

### Аудиосистема и звуки
Подсистема аудио обеспечивает аппаратно ускоренное воспроизведение звуковых эффектов (WAV, OGG, MP3):

| Функция | Сигнатура | Описание |
|---|---|---|
| `init_audio_device()` | `(): void` | Инициализирует звуковое устройство (WASAPI/ALSA/CoreAudio). |
| `close_audio_device()` | `(): void` | Закрывает звуковое устройство. |
| `is_audio_device_ready()` | `(): bool` | Проверяет готовность звукового устройства к воспроизведению. |
| `load_sound(path)` | `(string): i64` | Загружает звуковой сэмпл в память и возвращает дескриптор. |
| `unload_sound(snd)` | `(i64): void` | Освобождает ресурсы звукового сэмпла. |
| `play_sound(snd)` | `(i64): void` | Начинает воспроизведение звука. |
| `stop_sound(snd)` | `(i64): void` | Останавливает воспроизведение. |
| `pause_sound(snd)` | `(i64): void` | Ставит воспроизведение на паузу. |
| `resume_sound(snd)` | `(i64): void` | Возобновляет воспроизведение после паузы. |
| `is_sound_playing(snd)` | `(i64): bool` | Проверяет, проигрывается ли звук в данный момент. |
| `set_sound_volume(snd, volume)` | `(i64, f32): void` | Устанавливает громкость воспроизведения (от `0.0` до `1.0`). |

```rust
init_audio_device();
let laser_snd = load_sound("assets/laser.wav");
set_sound_volume(laser_snd, 0.8);
play_sound(laser_snd);

// При завершении игры:
unload_sound(laser_snd);
close_audio_device();
```
| Функция | Сигнатура | Описание |
|---|---|---|
| `is_key_down(key)` | `(i32): bool` | Зажата ли клавиша клавиатуры прямо сейчас. |
| `is_key_pressed(key)` | `(i32): bool` | Была ли клавиша нажата в текущем кадре. |
| `is_key_released(key)` | `(i32): bool` | Была ли клавиша отпущена в текущем кадре. |
| `is_key_up(key)` | `(i32): bool` | Не нажата ли клавиша. |
| `get_mouse_x()` | `(): i32` | Текущая координата мыши по оси X в окне. |
| `get_mouse_y()` | `(): i32` | Текущая координата мыши по оси Y в окне. |
| `is_mouse_button_down(btn)` | `(i32): bool` | Зажата ли кнопка мыши (`0` = Left, `1` = Right, `2` = Middle). |
| `is_mouse_button_pressed(btn)` | `(i32): bool` | Была ли нажата кнопка мыши в текущем кадре. |

**Стандартные модули констант ввода (`std/keys.ecs` и `std/mouse.ecs`):**

Вместо использования «магических чисел» рекомендуется импортировать стандартные модули констант времени компиляции:

```rust
import "std/keys.ecs";
import "std/mouse.ecs";

if is_key_down(KEY_SPACE) {
    // Прыжок
}

if is_key_down(KEY_W) || is_key_down(KEY_UP) {
    // Движение вперед
}

if is_mouse_button_pressed(MOUSE_BUTTON_LEFT) {
    // Выстрел или клик
}
```

Модуль `std/keys.ecs` содержит все коды клавиш (`KEY_A`..`KEY_Z`, `KEY_ZERO`..`KEY_NINE`, `KEY_LEFT`, `KEY_RIGHT`, `KEY_UP`, `KEY_DOWN`, `KEY_SPACE`, `KEY_ESCAPE`, `KEY_ENTER`, `KEY_F1`..`KEY_F12`).
Модуль `std/mouse.ecs` содержит кнопки мыши (`MOUSE_BUTTON_LEFT`, `MOUSE_BUTTON_RIGHT`, `MOUSE_BUTTON_MIDDLE` и др.) и стили курсора (`MOUSE_CURSOR_*`).

### Работа с цветами
Функция `rl_color(r, g, b, a)` упаковывает 4 байта (0–255) в 32-битное число, совместимое со структурой `Color` C ABI Raylib:

```rust
let red = rl_color(230, 41, 55, 255);
let dark_blue = rl_color(20, 24, 38, 255);
let white = rl_color(255, 255, 255, 255);
```

### Пример игрового цикла
```rust
component Ball {
    x: f32,
    y: f32,
    vx: f32,
    vy: f32,
    radius: f32,
    color: i32,
}

resource Time {
    dt: f32,
}

system PhysicsSystem {
    query(mut ball: Ball, time: Time) {
        ball.x += ball.vx * time.dt;
        ball.y += ball.vy * time.dt;
        if ball.x <= ball.radius || ball.x >= 800.0 - ball.radius {
            ball.vx = 0.0 - ball.vx;
        }
    }
}

system RenderSystem {
    query(ball: Ball) {
        draw_circle(ball.x, ball.y, ball.radius, ball.color);
    }
}

pipeline GamePipeline {
    stage Update {
        PhysicsSystem;
    }
    stage Draw {
        RenderSystem;
    }
}

fn main(): i32 {
    init_window(800, 600, "ECSLang Raylib Game");
    set_target_fps(60);

    let mut world = ecs::create_world();
    let b = world.spawn();
    world.set_Ball(b, 400.0, 300.0, 200.0, 150.0, 20.0, rl_color(255, 100, 100, 255));

    while !window_should_close() {
        let dt = get_frame_time();
        world.set_Time(dt);

        begin_drawing();
        clear_background(rl_color(20, 24, 38, 255));
        
        GamePipeline(world);
        
        draw_text("ECSLang 60 FPS", 10, 10, 20, rl_color(255, 255, 255, 255));
        end_drawing();
    }

    close_window();
    return 0;
}
```

---

## 13. Буфер команд и мутация мира (Command Buffer & Deferred Mutations)

Для безопасного создания, удаления и изменения компонентов сущностей прямо во время выполнения систем в ECSLang реализован механизм **Command Buffer**. Команды мутации накапливаются во встроенном потокобезопасном буфере и пакетом применяются вне циклов итерации систем.

### Получение текущей сущности (`Entity`)
В сигнатуре запроса системы можно объявить параметр типа `Entity` (например, `e: Entity`). В цикле системы переменная `e` содержит целочисленный идентификатор текущей обрабатываемой сущности:

```rust
system LifetimeSystem {
    query(e: Entity, mut lt: Lifetime, cmd: Commands) {
        lt.seconds -= 0.016;
        if lt.seconds <= 0.0 {
            cmd.despawn(e);
        }
    }
}
```

### Доступ к `Commands` в системах
Параметр `cmd: Commands` объявляется в `query(...)` систем движения или в `read(...)` систем-наблюдателей:
```rust
system GunnerSystem {
    query(e: Entity, pos: Position, cmd: Commands) {
        if is_key_pressed(32) { // Space
            let bullet = cmd.spawn();
            cmd.add_Position(bullet, pos.x, pos.y);
            cmd.add_Velocity(bullet, 500.0, 0.0);
        }
    }
}

system CollisionObserver {
    read(col: CollisionEvent, cmd: Commands) {
        cmd.despawn(col.target);
    }
}
```

### Методы `Commands`
| Метод | Сигнатура | Описание |
|---|---|---|
| `cmd.spawn()` | `(): Entity` | Выделяет уникальный идентификатор сущности и планирует добавление в мир. Возвращает созданный `Entity`. |
| `cmd.despawn(e)` | `(Entity): void` | Планирует гарантированное удаление сущности `e` из мира. |
| `cmd.add_Comp(e, args...)` | `(Entity, ...): void` | Планирует добавление/обновление компонента `Comp` для сущности `e`. |
| `cmd.set_Comp(e, args...)` | `(Entity, ...): void` | Синоним `add_Comp`. |
| `cmd.add(e, Comp(...))` | `(Entity, Comp): void` | Планирует добавление компонента через синтаксис конструктора. |
| `cmd.set(e, Comp(...))` | `(Entity, Comp): void` | Синоним `add(e, Comp(...))`. |
| `cmd.remove_Comp(e)` | `(Entity): void` | Планирует удаление компонента `Comp` из сущности `e`. |

### Применение команд в конвейере (`apply_commands`)
Команды применяются автоматически на границах стадий конвейера, либо явно с помощью инструкции `apply_commands;`:

```rust
pipeline GamePipeline {
    stage Simulation {
        GunnerSystem;
        LifetimeSystem;
        apply_commands; // Немедленно применяет отложенные спавны и деспавны
    }
    stage Render {
        RenderBulletsSystem;
    }
}
```

### Методы мутации в объекте `World`
Для ручного управления сущностями вне систем доступны методы:
- `world.spawn() -> Entity` — создать сущность и сразу поместить в пустой архетип 0.
- `world.despawn(e: Entity)` — удалить сущность $O(1)$ методом swap-remove из текущей таблицы архетипа.
- `world.apply_commands()` — принудительно применить все накопленные в буфере команды.

---

## 14. Справочник по CLI компилятора и отладке

Сборка, оптимизация и запуск программ производятся с помощью интерфейса командной строки ECS-Lang:

```bash
# Базовый синтаксис
ecs build <file.ecs> [options]
ecs run   <file.ecs> [options]
```

### Параметры командной строки
| Флаг | Описание |
|---|---|
| `-o <path>` | Задать путь к выходному исполняемому файлу (`.exe`). |
| `--release`, `-r` | Сборка в релизном режиме (максимальная оптимизация `-O3`, векторизация SIMD, оптимизации линковщика `/OPT:REF /OPT:ICF`, отсутствие ожидания клавиши Enter). |
| `-O0` | Отключение оптимизаций (по умолчанию в Debug-режиме). |
| `-O1`, `-O2`, `-O3` | Уровни оптимизации LLVM New Pass Manager. `-O3` включает Loop Vectorization, SLP Vectorization и разворачивание циклов. |
| `-Os`, `-Oz` | Оптимизация размера машинного кода. |
| `-g`, `--debug` | Генерация отладочной информации (CodeView / `.pdb` под Windows и DWARF), привязка строк исходного кода к инструкциям. |
| `--no-wait` | Отключение паузы «Press Enter to exit...» при завершении программы (активно по умолчанию для команды `run` и релизных сборок). |
| `--wait-key` | Принудительное ожидание нажатия клавиши Enter перед выходом (удобно при запуске кликом мыши в Windows Explorer). |
| `--emit-ir`, `--emit-llvm` | Выгрузка сгенерированного (и оптимизированного) LLVM IR представления (`.ll`). |

### Примеры использования

```bash
# 1. Сборка оптимизированного релиза
dotnet run --project src/ECSLang.CLI -- build game.ecs --release -o game.exe

# 2. Немедленная компиляция и запуск симуляции
dotnet run --project src/ECSLang.CLI -- run examples/command_buffer_test.ecs --release

# 3. Сборка с отладочной информацией для Visual Studio
dotnet run --project src/ECSLang.CLI -- build game.ecs -g -o game.exe
# Создает game.exe и game.pdb, готовые к пошаговой отладке с брейкпоинтами в VS/VS Code

# 4. Анализ векторизованного LLVM IR
dotnet run --project src/ECSLang.CLI -- build game.ecs --release --emit-ir
```

### Пошаговая отладка в Visual Studio / VS Code
Благодаря генерации метаданных CodeView и отладочных файлов `.pdb`, откомпилированные бинарники ECSLang можно открывать в Visual Studio:
1. Скомпилируйте проект с флагом `-g`: `ecs build main.ecs -g`.
2. Откройте Visual Studio, выберите **File -> Open -> Project/Solution** и укажите `main.exe`.
3. Откройте исходный файл `main.ecs` и установите точку останова (F9) на нужной строке системы или функции.
4. Нажмите F5 для запуска отладки — отладчик остановится на точной строке в `.ecs` файле.

---

## 15. Модули и многофайловые проекты (`import`)

Язык ECS-Lang позволяет организовывать проект в виде нескольких взаимосвязанных файлов с помощью директивы `import`.

### Синтаксис

Директива `import` указывается на верхнем уровне файла:

```rust
// 1. Импорт по относительному пути (в кавычках)
import "components.ecs";
import "physics/movement.ecs";

// 2. Импорт по имени модуля (без кавычек, автоматически ищет <name>.ecs)
import components;
```

### Правила разрешения импортов
1. **Относительный путь**: путь к импортируемому модулю всегда вычисляется относительно директории того файла, в котором написан `import`.
2. **Топологический порядок (Dependencies First)**: зависимости гарантированно компилируются и регистрируются перед файлами, которые их используют.
3. **Разрешение ромбовидных импортов (Diamond Dependencies)**: если несколько модулей импортируют один и тот же файл (например, `components.ecs`), компилятор гарантирует, что модуль загружается и парсится ровно один раз. Дублирования компонентов или структур не происходит.
4. **Защита от циклов (Cycle Detection)**: при возникновении циклического импорта (`a.ecs -> b.ecs -> a.ecs`) компилятор выводит понятную ошибку и прерывает компиляцию:
   ```
   [ERROR] b.ecs(1,1): Circular dependency detected: a.ecs -> b.ecs -> a.ecs
   ```

### Пример организации проекта

```
my_game/
├── components.ecs   # Объявления компонентов и ресурсов
├── systems.ecs      # Системы (import "components.ecs";)
├── pipeline.ecs     # Конвейер (import "systems.ecs";)
└── main.ecs         # Точка входа (import "pipeline.ecs";)
```

**Сборка и запуск проекта:**
Достаточно передать путь к главному входному файлу:
```bash
ecs run my_game/main.ecs --release
```
Компилятор автоматически обнаружит все связанные модули, выведет список разрешенных файлов и выполнит сборку:
```
[ECS-Lang] Compiling main.ecs [Release, -O3]...
[ECS-Lang] Resolved 4 module(s): components.ecs, systems.ecs, pipeline.ecs, main.ecs
[ECS-Lang] Successfully compiled to my_game/main.exe
[ECS-Lang] Running my_game/main.exe...
```

---

## 16. Строковые операции, интерполяция и математика

### 16.1. Конкатенация строк и приведение типов
В ECSLang оператор `+` перегружен для работы со строками (`string`). Если хотя бы один операнд является строкой, второй операнд автоматически конвертируется в строковое представление:

```rust
let name = "Hero";
let score: i32 = 100;
let health: f32 = 95.5;

// Конкатенация строки со строкой
let full = "Hello, " + name;

// Конкатенация строки с числами и булевыми значениями
let s1 = "Score: " + score;           // "Score: 100"
let s2 = "HP: " + health;             // "HP: 95.50"
let s3 = 10 + " points";              // "10 points"
let s4 = "Ready: " + true;            // "Ready: true"
```

### 16.2. Функция `to_string` и методы
Для явного преобразования любого скалярного значения (`i32`, `f32`, `bool`, `entity`) в строку поддерживается как глобальная функция `to_string(x)`, так и метод `x.to_string()`:

```rust
let n: i32 = 42;
let s_fn = to_string(n);
let s_method = n.to_string();
```

### 16.3. Длина строки
Длину строки в байтах можно узнать несколькими эквивалентными способами:
- Свойство: `s.len` или `s.length`
- Метод: `s.len()` или `s.length()`
- Функция: `str_len(s)`

```rust
let text = "Hello";
let l1: i32 = text.len;      // 5
let l2: i32 = text.len();    // 5
let l3: i32 = str_len(text); // 5
```

### 16.4. Сравнение строк
Строки сравниваются по значению (через CRT `strcmp`), а не по указателю:
- `s1 == s2` — возвращает `true`, если содержимое строк побайтово совпадает.
- `s1 != s2` — возвращает `true`, если строки различаются.

```rust
let a = "antigravity";
let b = "antigravity";
if a == b {
    println("Strings are identical");
}
```

### 16.5. Интерполяция строк
ECSLang поддерживает два популярных стиля интерполяции:
1. **Стиль C#** с префиксом `$`:
   ```rust
   let info = $"Player: {player_name} (Level {level}), Speed: {speed}";
   ```
2. **Стиль Python** с префиксом `f`:
   ```rust
   let msg = f"Next Level: {level + 1}, Speed Boost: {speed + 2.5}";
   ```

Внутри фигурных скобок `{...}` допускаются произвольные валидные выражения языка (арифметика, вызовы функций, обращение к полям). Интерполяция разворачивается компилятором на этапе синтаксического анализа в цепочку конкатенаций с нулевыми накладными расходами.

### 16.6. Встроенная математическая библиотека
Язык предоставляет набор встроенных функций, преобразуемых в высокоэффективные инструкции процессора или CRT-вызовы:

| Функция | Сигнатура | Описание |
|---|---|---|
| `sqrt(x)` | `(f32): f32` | Квадратный корень числа. |
| `sin(x)` | `(f32): f32` | Синус угла в радианах. |
| `cos(x)` | `(f32): f32` | Косинус угла в радианах. |
| `floor(x)` | `(f32): f32` | Округление вниз до ближайшего целого. |
| `ceil(x)` | `(f32): f32` | Округление вверх до ближайшего целого. |
| `abs(x)` | `(T): T` | Абсолютное значение (`i32` или `f32`). |
| `min(a, b)` | `(T, T): T` | Минимальное из двух значений (`i32` или `f32`). |
| `max(a, b)` | `(T, T): T` | Максимальное из двух значений (`i32` или `f32`). |
| `clamp(v, min, max)` | `(T, T, T): T` | Ограничение величины `v` диапазоном `[min, max]`. |
| `rand()` | `(): i32` | Псевдослучайное целое число. |
| `rand_range(min, max)` | `(i32, i32): i32` | Псевдослучайное целое число в диапазоне `[min, max]` включительно. |

---

## 15. Встроенный визуальный ECS-профайлер и инспектор (F1)

ECSLang включает нативный встроенный визуальный профайлер архитектуры ECS и инспектор состояния мира в реальном времени.

### 15.1. Вызов и активация
Профайлер вызывается в цикле отрисовки кадра (между `begin_drawing()` и `end_drawing()`):
```rust
world.render_profiler();
// или
render_profiler(world);
```

### 15.2. Поведение и управление
- **Переключение видимости**: Нажатие клавиши **F1** (код 290 в Raylib) переключает отображение полупрозрачного HUD-оверлея на экране.
- **Безопасный Fallback (Headless / Console)**: Если окно Raylib не инициализировано (`IsWindowReady() == false`), вызов функции мгновенно и безопасно возвращает управление без системных ошибок или накладных расходов.

### 15.3. Отображаемые метрики
1. **Кадровая производительность**:
   - Текущий FPS (Raylib `GetFPS()`).
   - Время кадра в миллисекундах (Frame Time с точностью до десятых).
2. **Метрики мира ECS**:
   - `Live entities`: суммарное число активных сущностей во всех архетипах мира.
   - `Archetypes`: общее число зарегистрированных в приложении архетипов данных.
3. **Инспектор архетипов**:
   - Битовая маска компонентов архетипа (Hex ID).
   - Заполненность чанков: количество сущностей в первом чанке по отношению к его емкости (`N / capacity`).
---

## 17. Хэш-таблицы (`HashMap<K, V>`) и именованная индексация сущностей

ECSLang включает встроенные обобщенные хэш-таблицы с открытой адресацией (Open Addressing) и линейным пробированием (Linear Probing), а также нативную интеграцию именованной индексации сущностей в `World`.

### 17.1. Создание и синтаксис типов

Тип хэш-таблицы записывается как `HashMap<K, V>` или `Map<K, V>`. Создание экземпляра выполняется вызовом конструктора `HashMap<K, V>()` или `Map<K, V>()`:

```rust
let mut scores: HashMap<string, i32> = HashMap<string, i32>();
let mut names: Map<i32, string> = Map<i32, string>();
```

### 17.2. Методы и операции с `HashMap<K, V>`

| Операция / Метод | Сигнатура / Пример | Описание |
|---|---|---|
| `.insert(key, val)` | `(K, V): void` | Добавляет или обновляет пару ключ-значение. При коэффициенте заполнения $> 70\%$ происходит удвоение емкости. |
| `.get(key)` | `(K): V` | Возвращает значение по ключу (или значение по умолчанию, если ключ не найден). |
| `map[key]` | Чтение по индексу | Эквивалентно `.get(key)`. |
| `map[key] = val` | Запись по индексу | Эквивалентно `.insert(key, val)`. |
| `.contains(key)` | `(K): bool` | Проверяет наличие ключа в таблице. |
| `.remove(key)` | `(K): bool` | Удаляет ключ из таблицы с установкой Tombstone; возвращает `true`, если ключ был найден. |
| `.len()` | `(): i32` | Возвращает количество активных элементов. |
| `.capacity()` | `(): i32` | Возвращает текущую емкость слотов таблицы. |
| `.clear()` | `(): void` | Очищает таблицу с сохранением выделенного буфера слотов. |

Пример использования:
```rust
let mut scores = HashMap<string, i32>();
scores.insert("Alice", 100);
scores["Bob"] = 200;

if scores.contains("Alice") {
    println($"Alice score: {scores["Alice"]}");
}

scores.remove("Bob");
println($"Remaining players: {scores.len()}");
```

### 17.3. Именованная индексация сущностей в `World`

Каждый контекст `World` содержит встроенную хэш-таблицу `HashMap<string, entity>`, позволяющую присваивать сущностям человекочитаемые строковые имена и мгновенно находить их за $O(1)$:

```rust
let player = world.spawn();
world.set_name(player, "Player");

let boss = world.spawn();
world.set_name(boss, "DragonBoss");

if world.has_name("Player") {
    let hero = world.get_by_name("Player");
    // Работа с найденной сущностью hero
}
```

| Метод `World` | Сигнатура | Описание |
|---|---|---|
| `world.set_name(e, name)` | `(entity, string): void` | Регистрирует имя сущности в индексе мира. |
| `world.get_by_name(name)` | `(string): entity` | Возвращает ID сущности по ее уникальному имени. |
| `world.has_name(name)` | `(string): bool` | Проверяет, зарегистрирована ли в мире сущность с данным именем. |

---

## 18. Аренное управление памятью строк (`World` String Arena)

Для устранения фрагментации системной кучи и исключения тысяч вызовов системных аллокаторов (`malloc`/`free`) в игровом цикле 60 FPS, каждый изолированный мир `World` оснащен встроенной монолитной ареной строк (Bump-pointer String Arena).

### 18.1. Принцип работы
- **Автоматическая аллокация в арене**: Любая операция конкатенации (`+`, `str_concat`), форматирования (`to_string`) или интерполяции (`$"..."`, `f"..."`), выполняемая в контексте мира (внутри систем, пайплайнов или функций, оперирующих `world`), автоматически выделяет память из активного чанка арены мира.
- **Монолитные чанки (64 КБ)**: Память выделяется крупными блоками по 64 КБ со связыванием в односвязный список. Внутри чанка смещение (`bump pointer`) сдвигается за 2-3 процессорные инструкции без системных вызовов ОС.
- **Мгновенный сброс (Instant Frame Reset)**: В конце или начале каждого кадра приложение может вызвать `world.reset_string_arena()`. Это сбрасывает счетчики `used` во всех чанках на ноль за доли микросекунды, позволяя повторно использовать всю строковую память следующего кадра без фрагментации.

### 18.2. Методы управления ареной строк

| Метод `World` | Сигнатура | Описание |
|---|---|---|
| `world.reset_string_arena()` | `(): void` | Мгновенно сбрасывает указатель смещения во всех чанках строковой арены мира, подготавливая их к переиспользованию в следующем кадре. |
| `world.alloc_string(capacity)` | `(i32): string` | Напрямую выделяет сырой строковый буфер указанного размера из строковой арены мира. |

Пример использования в игровом цикле:
```rust
fn main(): i32 {
    let mut world = ecs::create_world();

    while !window_should_close() {
        // Формирование динамических UI-строк и отладочных сообщений:
        let hud_text = $"FPS: {get_fps()}, Entities: {world.has_name("Player")}";
        draw_text(hud_text, 10, 10, 20, 0xFFFFFFFF);

        // В конце кадра вся строковая память освобождается мгновенно:
        world.reset_string_arena();
    }

---

## 19. Безопасные типы `Option<T>` и `Result<T, E>` с сопоставлением с образцом (Pattern Matching)

Для исключения `null`-указателей, неконтролируемых сбоев и паник времени выполнения при обращении к отсутствующим элементам коллекций и мира, в ECSLang введены обобщенные типы разметченных объединений (Tagged Unions): `Option<T>` и `Result<T, E>`.

### 19.1. Тип `Option<T>`

Представляет значение, которое может либо присутствовать (`Some(val)`), либо отсутствовать (`None`):

```rust
let present: Option<i32> = Some(42);
let absent: Option<i32> = None;
```

#### Методы `Option<T>`:
| Метод | Возвращает | Описание |
|---|---|---|
| `opt.is_some()` | `bool` | Возвращает `true`, если значение присутствует. |
| `opt.is_none()` | `bool` | Возвращает `true`, если значение отсутствует (`None`). |
| `opt.unwrap()` | `T` | Возвращает значение внутри `Some(val)`. Если `None`, вызывает контролируемую панику с выводом ошибки и аварийным завершением. |
| `opt.unwrap_or(default)` | `T` | Возвращает значение `val`, либо переданное значение по умолчанию `default`, если `opt` равен `None`. |

Сравнение с `None`:
```rust
if opt != None {
    println("Значение присутствует!");
}
```

### 19.2. Тип `Result<T, E>`

Используется для надежной обработки ошибок и возврата статуса операций. Содержит вариант успешного результата `Ok(val)` либо вариант ошибки `Err(err)`:

```rust
let ok_res: Result<i32, string> = Ok(200);
let err_res: Result<i32, string> = Err("Неверный ввод");
```

#### Методы `Result<T, E>`:
| Метод | Возвращает | Описание |
|---|---|---|
| `res.is_ok()` | `bool` | Возвращает `true`, если операция успешна (`Ok`). |
| `res.is_err()` | `bool` | Возвращает `true`, если произошла ошибка (`Err`). |
| `res.unwrap()` | `T` | Возвращает значение `Ok(val)`. Если `Err`, вызывает панику с выводом ошибки. |
| `res.unwrap_err()` | `E` | Возвращает значение ошибки `Err(err)`. Если `Ok`, вызывает панику. |
| `res.unwrap_or(default)` | `T` | Возвращает значение `val` из `Ok`, либо `default`, если результат содержит `Err`. |

### 19.3. Сопоставление с образцом (`match`)

Конструкция `match` позволяет безопасно распаковывать `Option` и `Result` с автоматическим связыванием переменной (variable binding):

```rust
// Распаковка Option:
match opt {
    Some(val) => {
        println("Получено значение: %d", val);
    }
    None => {
        println("Значение отсутствует.");
    }
}

// Распаковка Result:
match res {
    Ok(val) => {
        println("Успех: %d", val);
    }
    Err(err) => {
        println("Ошибка: %s", err);
    }
}
```

### 19.4. Безопасные операции коллекций и мира

Встроенные коллекции и мир предоставляют безопасные методы поиска, возвращающие `Option`:

1. **Динамические массивы (`Vec<T>` / `[T]`)**:
   ```rust
   let mut list = Vec<i32>();
   list.push(10);
   let elem = list.get(0); // Option<i32> -> Some(10)
   let miss = list.get(9); // Option<i32> -> None
   ```

2. **Хэш-таблицы (`HashMap<K, V>`)**:
   ```rust
   let mut map = HashMap<string, i32>();
   map.insert("apples", 15);
   let fruit = map.find("apples"); // Option<i32> -> Some(15)
   let none = map.find("cherries"); // Option<i32> -> None
   ```

3. **Мир ECS (`World`)**:
   ```rust
   let hero = world.spawn();
   world.set_name(hero, "Hero");

    let found = world.find("Hero"); // Option<entity> -> Some(hero)
    let missing = world.find("Villain"); // Option<entity> -> None
    ```

### 19.5. Оператор распространения ошибок и опциональных значений (`?`)

Оператор `?` (Try Operator) кардинально упрощает работу с `Result<T, E>` и `Option<T>`, устраняя необходимость ручного разбора через `match` при каскадных вызовах.

#### Семантика для `Result<T, E>`:
- Если выражение возвращает `Ok(val)`, оператор распаковывает его и отдает значение `val` (типа `T`) дальше в выражение.
- Если выражение возвращает `Err(err)`, функция немедленно прерывает выполнение и возвращает `Err(err)` на уровень выше.
- **Требование типизации**: объемлющая функция должна возвращать `Result<_, E>` с точно таким же типом ошибки `E`.
- **Использование в `main()`**: если ошибка распространяется в точку входа программы `main()`, рантайм форматирует сообщение об ошибке, печатает его в консоль и завершает процесс с кодом возврата `1`.

```rust
fn read_config(): Result<Config, string> {
    let file = open_file("config.json")?; // При ошибке сразу вернет Err(err)
    let data = parse_json(file)?;         // При ошибке сразу вернет Err(err)
    return Ok(data);
}

fn main(): i32 {
    let cfg = read_config()?; // В main при ошибке распечатает [Error] ... и сделает exit(1)
    println($"Config loaded: {cfg.title}");
    return 0;
}
```

#### Семантика для `Option<T>`:
- Если выражение возвращает `Some(val)`, оператор распаковывает значение `val` (типа `T`).
- Если выражение возвращает `None`, функция немедленно завершается возвратом `None`.
- **Требование типизации**: объемлющая функция должна возвращать `Option<_>`. В функции `main()` возврат `None` приводит к выводу ошибки и завершению с кодом `1`.

```rust
fn get_user_avatar_url(user_id: i32): Option<string> {
    let profile = find_profile(user_id)?; // При None немедленно вернет None
    let avatar = profile.avatar?;          // При None немедленно вернет None
    return Some(avatar.url);
}
```


---

## 20. Стандартная библиотека ECS GUI (`std/gui.ecs`)

ECSLang предоставляет богатую стандартную библиотеку графического интерфейса, полностью написанную на чистом **ECSLang** без жестко закодированных в компилятор C#-классов виджетов. Библиотека следует принципу **Data-Oriented & Pure ECS Architecture**: любой элемент интерфейса — это обычная сущность (`entity`), его свойства хранятся в ECS-компонентах, интерактивность обеспечивается системами обновления (`Update`), а отрисовка — фазовыми системами рендеринга (`Render`), объединяемыми в `pipeline GUIPipeline`.

### 20.1. Подключение библиотеки
```rust
import "std/gui.ecs";
```
Благодаря автоматическому поиску модулей `ProjectLoader`, путь `std/gui.ecs` разрешается относительно стандартной библиотеки компилятора или корня проекта.

### 20.2. Компоненты GUI (22 компонента)

#### 1. Геометрия и компоновка
- `UIRect { x: f32, y: f32, w: f32, h: f32 }`: абсолютные экранные координаты и габариты.
- `UIPos { x: f32, y: f32 }`: относительное смещение позиции.
- `UISize { width: f32, height: f32, min_w: f32, min_h: f32, max_w: f32, max_h: f32 }`: ограничения размеров.
- `UIPadding { left: f32, right: f32, top: f32, bottom: f32 }`: внутренние отступы контейнера.
- `UIMargin { left: f32, right: f32, top: f32, bottom: f32 }`: внешние отступы.
- `UIAnchor { min_x: f32, min_y: f32, max_x: f32, max_y: f32 }`: привязка к границам родителя (от 0.0 до 1.0).
- `UILayout { layout_type: i32, spacing: f32, alignment: i32 }`: тип авто-компоновки (0 = None, 1 = Vertical, 2 = Horizontal, 3 = Grid).
- `UIZOrder { z_index: i32 }`: глубина отображения для сортировки слоев.

#### 2. Стили и оформление
- `UIBackground { color: i32, border_color: i32, border_width: f32, border_radius: f32 }`: цвет заливки, рамка, скругление.
- `UIShadow { offset_x: f32, offset_y: f32, blur: f32, color: i32 }`: тень элемента.
- `UIText { text: string, font_size: i32, color: i32, alignment: i32 }`: текстовая надпись (0 = Left, 1 = Center, 2 = Right).
- `UITexture { texture_id: i64, tint: i32 }`: текстурное изображение.
- `UITooltip { text: string, target_entity: i32, delay_ms: f32, elapsed_ms: f32, is_visible: bool }`: всплывающая подсказка.

#### 3. Виджеты и состояния
- `UIState { is_hovered: bool, is_pressed: bool, is_focused: bool, is_disabled: bool }`: универсальное состояние интерактивности.
- `UIButton { normal_color: i32, hover_color: i32, pressed_color: i32 }`: кнопка.
- `UICheckbox { is_checked: bool, label: string }`: флажок с надписью.
- `UISlider { min_val: f32, max_val: f32, current_val: f32, handle_size: f32, is_dragging: bool }`: ползунок выбора значения.
- `UIProgressBar { progress: f32, bar_color: i32, background_color: i32 }`: индикатор прогресса (от 0.0 до 1.0).
- `UIPanel { title: string, is_movable: bool, is_dragging: bool, drag_offset_x: f32, drag_offset_y: f32 }`: перемещаемое окно / панель.
- `UIRadioButton { group_id: i32, is_selected: bool, label: string }`: переключатель группы опций.
- `UIToggleSwitch { is_on: bool, on_color: i32, off_color: i32 }`: тумблер (on/off).
- `UIBadge { text: string, badge_color: i32, text_color: i32 }`: информационный бейдж / счетчик.
- `UISeparator { is_vertical: bool, thickness: f32, color: i32 }`: разделительная черта.
- `UIInputField { text: string, placeholder: string, cursor_pos: i32, is_editing: bool }`: поле ввода текста.
- `UIScrollArea { scroll_x: f32, scroll_y: f32, max_scroll_x: f32, max_scroll_y: f32 }`: прокручиваемая область.

---

### 20.3. Реактивные события GUI

- `event UIEventClick { entity: i32, mouse_button: i32 }`: клик мыши по элементу.
- `event UIEventValueChanged { entity: i32, new_value: f32 }`: изменение числового значения (слайдер).
- `event UIEventToggle { entity: i32, state: bool }`: переключение флага / тумблера.
- `event UIEventDrag { entity: i32, delta_x: f32, delta_y: f32 }`: перетаскивание элемента.

---

### 20.4. Системы и конвейер `GUIPipeline`

Стандартный конвейер GUI разделен на две фазы:
```rust
pipeline GUIPipeline {
    stage Update {
        UIHoverSystem;
        UIPanelDragSystem;
        UISliderSystem;
        UICheckboxSystem;
        UIToggleSwitchSystem;
        UIRadioButtonSystem;
    }
    stage Render {
        UIPanelRenderSystem;
        UIBackgroundRenderSystem;
        UIButtonRenderSystem;
        UICheckboxRenderSystem;
        UIRadioButtonRenderSystem;
        UISliderRenderSystem;
        UIProgressBarRenderSystem;
        UIToggleSwitchRenderSystem;
        UISeparatorRenderSystem;
        UILabelRenderSystem;
        UITooltipRenderSystem;
    }
}
```

Все системы рендеринга автоматически безопасны в headless / тестовом режиме: если графическое окно Raylib не инициализировано (`is_window_ready() == false`), функции отрисовки безопасно завершаются без сбоев OpenGL контекста.

---

### 20.5. Вспомогательные функции виджетов (`widgets.ecs`)

Для быстрого спавна готовых виджетов предоставляются хелперы:
```rust
// Окно / панель
let panel = gui_panel(world, 50.0, 50.0, 400.0, 500.0, "Настройки игры");

// Текстовая надпись
let lbl = gui_label(world, 70.0, 90.0, "Громкость звука:", 16, 0xFFFFFFFF);

// Кнопка
let btn = gui_button(world, 70.0, 125.0, 150.0, 36.0, "Сохранить");

// Флажок (Checkbox)
let cb = gui_checkbox(world, 70.0, 195.0, "Включить VSync", true);

// Тумблер (Toggle Switch)
let sw = gui_toggle_switch(world, 70.0, 260.0, true);

// Радио-кнопка (Radio Button)
let rb = gui_radio_button(world, 70.0, 295.0, "Сложный режим", 1, false);

// Ползунок (Slider)
let sld = gui_slider(world, 70.0, 370.0, 320.0, 24.0, 0.0, 100.0, 75.0);

// Полоса загрузки (Progress Bar)
let pb = gui_progress_bar(world, 70.0, 485.0, 320.0, 16.0, 0.65);

// Разделитель
let sep = gui_separator(world, 70.0, 330.0, 320.0, false);

// Всплывающая подсказка
gui_tooltip(world, btn, "Нажмите для сохранения изменений");
```

---

## 21. Замыкания, лямбда-выражения и методы высшего порядка (Closures & Lambdas)

ECSLang поддерживает первоклассные анонимные функции (лямбды) и замыкания с компактным синтаксисом параметров в вертикальных чертах `|e| ...` и `|| ...`.

### 21.1. Синтаксис объявления

#### 1. Однострочные лямбда-выражения (Expression Body):
```rust
let double_it = |x: i32| x * 2;
let res = double_it(21); // 42
```

#### 2. Многопараметрические блочные лямбды (Block Body):
```rust
let add_two = |a: i32, b: i32| -> i32 {
    return a + b;
};
let sum = add_two(17, 25); // 42
```

#### 3. Лямбды без параметров:
```rust
let get_answer = || 42;
let ans = get_answer(); // 42
```

### 21.2. Функциональные типы и функции высшего порядка
Тип замыкания или функции аннотируется как `fn(T1, T2): Ret` или `fn(T1, T2) -> Ret`:
```rust
fn apply(x: i32, f: fn(i32): i32): i32 {
    return f(x);
}

fn main() {
    let res = apply(10, |n: i32| n * 5); // 50
}
```

### 21.3. Захват контекста (Environment Capturing)
Замыкания в ECSLang используют унифицированный **Fat Pointer** (`{ ptr fn, ptr env }`) со стековым размещением фрейма окружения (Zero Heap Allocation):

- **Неизменяемый захват (Immutable)**: чтение внешних локальных переменных.
- **Изменяемый захват (Mutable)**: изменение внешних `mut` переменных напрямую на стеке вызывающей функции.

```rust
let factor = 10;
let scale = |x: i32| x * factor; // Захват factor

let mut total = 0;
let acc = |step: i32| {
    total += step; // Прямая мутация переменной total
};
acc(10);
acc(25);
// total теперь равен 35
```

### 21.4. Методы высшего порядка над динамическими массивами (`Vec<T>`)
Коллекции `Vec<T>` / `[T]` предоставляют встроенные методы функциональной обработки элементов:

| Метод | Сигнатура аргумента | Возвращает | Описание |
|---|---|---|---|
| `.for_each(c)` | `fn(T): void` | `void` | Итерируется по элементам и вызывает замыкание |
| `.map(c)` | `fn(T): R` | `Vec<R>` | Преобразует каждый элемент в новый массив |
| `.filter(c)` | `fn(T): bool` | `Vec<T>` | Фильтрует элементы по предикату |
| `.any(c)` | `fn(T): bool` | `bool` | Возвращает `true`, если хотя бы один элемент удовлетворяет условию |
| `.all(c)` | `fn(T): bool` | `bool` | Возвращает `true`, если все элементы удовлетворяют условию |
| `.find(c)` | `fn(T): bool` | `Option<T>` | Возвращает `Some(val)` первого совпадения или `None` |

```rust
let mut nums = Vec<i32>();
nums.push(1);
nums.push(2);
nums.push(3);
nums.push(4);

// for_each
let mut sum = 0;
nums.for_each(|n: i32| { sum += n; });

// map
let doubled = nums.map(|n: i32| n * 2);

// filter
let evens = nums.filter(|n: i32| n % 2 == 0);

// any / all
let has_gt_2 = nums.any(|n: i32| n > 2); // true
let all_positive = nums.all(|n: i32| n > 0); // true

// find
match nums.find(|n: i32| n == 3) {
    Some(val) => println("Found 3!"),
    None => println("Not found")
}
```

---

## 22. Высокоточные интринсики времени (`stopwatch_start`, `stopwatch_ms`)

Для прецизионного замера времени выполнения систем, бенчмаркинга Big Data алгоритмов и профилирования без накладных расходов виртуальных машин, в стандартную библиотеку рантайма включены функции прямого аппаратного таймера:

| Функция | Сигнатура | Возвращает | Описание |
|---|---|---|---|
| `stopwatch_start()` | `(): i64` | `i64` | Запрашивает текущее значение аппаратного счетчика таймера высокого разрешения (`QueryPerformanceCounter` на Windows, `clock_gettime(CLOCK_MONOTONIC)` на POSIX). |
| `stopwatch_ms(start)` | `(start: i64): f32` | `f32` | Вычисляет точное количество прошедших миллисекунд с момента вызова `start` с нулевым оверхедом. |

### Пример использования:
```rust
let start = stopwatch_start();

// Высоконагруженная операция или пайплайн
for i in 0..1000000 {
    do_work();
}

let elapsed_ms = stopwatch_ms(start);
println($"Elapsed: {elapsed_ms:.2f} ms");
```

---

## 23. Сетевая подсистема и TCP Framing (`std/net.ecs`)

Модуль стандартной библиотеки `std/net.ecs` предоставляет pure ECS сетевой стек с неблокирующим I/O, поддержкой TCP/UDP, реактивными событиями и протоколом фрейминга с 4-байтовым префиксом длины.

### 23.1. Сетевые компоненты
- `TcpListener`: `{ port: i32, fd: i32, is_active: bool }` — слушающий серверный сокет.
- `TcpConnection`: `{ fd: i32, peer_ip: string, peer_port: i32, is_connected: bool, rx_buffer: Vec<u8>, expected_len: i32, current_offset: i32 }` — клиентское или серверное TCP-соединение с внутренним буфером приема.
- `UdpSocket`: `{ port: i32, fd: i32, is_active: bool }` — сокет дейтаграмм UDP.
- `SocketClosing`: `{ }` — маркер запроса закрытия сокета для систем-деструкторов.

### 23.2. Функции TCP Framing и буферизации
| Функция | Сигнатура | Возвращает | Описание |
|---|---|---|---|
| `net_tcp_send_framed(fd, payload)` | `(i32, string): i32` | `i32` | Упаковывает 4-байтовый размер (`htonl`) перед телом `payload` и атомарно передает фрейм через сокет. |
| `net_tcp_recv_append(fd, buf, max_len)` | `(i32, Vec<u8>, i32): Vec<u8>` | `Vec<u8>` | Неблокирующее считывание входящих сырых байт напрямую в конец буфера с динамическим ростом емкости. |
| `net_buffer_read_i32(buf, offset)` | `(Vec<u8>, i32): i32` | `i32` | Безопасное извлечение 32-битного целого с конвертацией из сетевого порядка байт (`ntohl`). При выходе за границы возвращает `-1`. |
| `net_buffer_extract_str(buf, offset, len)` | `(Vec<u8>, i32, i32): string` | `string` | Извлекает срез полезной нагрузки указанной длины с нуль-терминатором. |
| `net_buffer_drain(buf, count)` | `(Vec<u8>, i32): Vec<u8>` | `Vec<u8>` | Сдвигает оставшиеся байты к началу буфера через `memmove`, сохраняя выделенную емкость без реаллокаций. |




