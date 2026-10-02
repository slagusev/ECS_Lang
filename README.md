# ⚡ ECSLang: High-Performance Pure ECS & Data-Oriented Systems Language

<p align="center">
  <img src="https://img.shields.io/badge/LLVM-20.1.2-blue.svg?style=for-the-badge&logo=llvm" alt="LLVM 20" />
  <img src="https://img.shields.io/badge/Paradigm-Pure_ECS_&_DOD-red.svg?style=for-the-badge" alt="Pure ECS" />
  <img src="https://img.shields.io/badge/GC-0%25_Zero_Cost-brightgreen.svg?style=for-the-badge" alt="Zero GC" />
  <img src="https://img.shields.io/badge/Platform-Windows_|_Linux_|_macOS-blueviolet.svg?style=for-the-badge" alt="Cross-Platform" />
  <img src="https://img.shields.io/badge/License-MIT-orange.svg?style=for-the-badge" alt="License" />
</p>

---

## 🚀 Overview / Обзор

**ECSLang** is an ultra-fast, compiled systems programming language designed from the ground up around **Pure Entity Component System (ECS)** and **Data-Oriented Design (DOD)** principles. Backed by **LLVM 20**, ECSLang achieves bare-metal CPU throughput with **0% Garbage Collector pauses, 0% OOP inheritance bloat**, and zero-cost high-level abstractions.

> *"What if ECS was not a third-party library, but a first-class language citizen compiled directly into cache-coherent Structure of Arrays (SoA) machine code?"*

### 🌟 Key Architectural Pillars:
- **Zero-GC & Bare-Metal Speed**: Deterministic memory management without garbage collection pauses or hidden allocations.
- **First-Class Isolated Worlds**: Multi-world architecture (`let mut world = ecs::create_world()`) eliminates global state.
- **Columnar Archetype (SoA) Storage**: Entities with matching component signatures live in dense contiguous memory blocks tuned for L1/L2 CPU cache lines and SIMD auto-vectorization.
- **Bevy-Grade `parallel auto` Scheduler**: Compiler builds a static dependency Directed Acyclic Graph (DAG) based on component read/write access masks and executes collision-free systems across CPU thread pools without data races.
- **Bulk Spawn (`world.spawn_with`)**: Instant single-pass archetype allocation reducing entity migrations from $O(N \times M)$ to **0**.
- **Zero-Copy String Views (`str_view`)**: Free substring parsing and search via CPU register views (`{ ptr, len }`) without `malloc` or `memcpy`.
- **Integrated Standard Library**: Built-in 2D Graphics & Audio (Raylib), Pure ECS GUI toolkit, Non-blocking Lock-Free Networking, and 64-bit Enterprise File I/O.

---

## 📊 Apples-to-Apples Benchmarks (Sandy Bridge Legacy Rig)

Real-world fair comparison conducted on consumer legacy hardware (Intel Sandy Bridge, DDR3 RAM) showcasing pure throughput against **C# (.NET 9 JIT / AOT)**.

### 1. Enterprise File I/O (1,000,000 Records: Open → Lock → Append → Close)
Each iteration performs a complete atomic thread-safe write cycle to simulate high-concurrency enterprise auditing.

| Metric | C# (.NET 9) | ECSLang (LLVM -O3) | Speedup / Advantage |
|---|---|---|---|
| **Write 1,000,000 Lines (Atomic)** | 5,420 ms | **3,190 ms** | **1.70x Faster** |
| **Write Throughput** | 184,500 lines/sec | **313,479 lines/sec** | **+70% Throughput** |
| **Read 74.0 MB File** | 189 ms | **72 ms** | **2.62x Faster** |
| **Read Throughput** | 391.5 MB/sec | **1,027.8 MB/sec** | **> 1.0 GB/sec (Cold Disk)** |
| **Runtime Memory Overhead** | 94 MB | **1.2 MB** | **78x Lower Footprint** |

### 2. In-Memory OLAP Big Data Analytics (50,000 Log Records)
Processing structured server access logs into columnar SoA archetypes with parallel multi-system aggregation:

| Pipeline Stage | Naive Approach | ECSLang Optimized Core | Improvement |
|---|---|---|---|
| **Batch Disk Generation (Step A)** | 4,200 ms (Unbuffered) | **4.22 ms** (Batch Buffer) | **~1000x Faster** |
| **Log Ingestion & Parsing (Step C)** | 5,040 ms (String Substrings) | **142 ms** (Zero-Copy `str_view`) | **35x Faster** |
| **SoA Memory Migrations** | 250,000 migrations | **0 migrations** (`spawn_with`) | **100% Elimination** |
| **Parallel Auto Query (Step D)** | 1.84 ms | **0.37 ms** (`parallel auto` DAG) | **5.0x Faster** |
| **In-Memory Analytics Throughput** | 27.1 M records/sec | **136.46 Million records / sec** | **Sub-millisecond OLAP** |

---

## 💻 Code Showcase / Примеры кода

### 1. In-Memory OLAP Big Data Engine (`olap_bigdata_analyzer.ecs`)
Demonstrating columnar components, bulk entity creation, zero-copy string views, and parallel auto-scheduled systems.

```rust
// 1. Pure Columnar ECS Components (SoA Memory Layout)
component LogTimestamp { time: str_view }
component LogLevel     { is_info: bool, is_error: bool }
component HttpMethod   { is_get: bool }
component ResponseTime { ms: f32 }
component StatusCode   { code: i32 }

// 2. Data-Race Free Global Aggregators
resource ErrorStats   { count: i32 }
resource LatencyStats { total_ms: f32 }

// 3. Analytics Systems
system FilterErrorsSystem {
    query(lvl: LogLevel, mut errs: ErrorStats) {
        if lvl.is_error {
            errs.count += 1;
        }
    }
}

system MetricsAggregatorSystem {
    query(rt: ResponseTime, mut lat: LatencyStats) {
        lat.total_ms += rt.ms;
    }
}

// 4. Multi-Threaded Query Pipeline (Topological DAG Auto-Scheduler)
pipeline OlapQueryPipeline {
    stage Analysis {
        // Compiler DAG automatically places non-conflicting systems
        // into Layer 0, executing across all CPU threads concurrently!
        parallel auto {
            FilterErrorsSystem;
            MetricsAggregatorSystem;
        }
    }
}

fn main(): i32 {
    let mut world = ecs::create_world();
    world.set_ErrorStats(0);
    world.set_LatencyStats(0.0);

    // Read log and parse with Zero-Copy String Views
    let content = file_read_text("server_bigdata.log").unwrap();
    let line: str_view = content.view_substring(0, 52);

    let is_err = line.contains("[ERROR]");
    let is_get = line.contains("GET");
    let ts_view = line.view_substring(0, 10); // 0 bytes allocated in heap!

    // Bulk Spawn: direct single-pass insertion into target SoA archetype
    let entity = world.spawn_with(
        LogLevel(!is_err, is_err),
        HttpMethod(is_get),
        LogTimestamp(ts_view),
        StatusCode(200),
        ResponseTime(45.2)
    );

    // Execute parallel OLAP query
    OlapQueryPipeline(world);

    let errors = world.get_ErrorStats();
    println($"Total identified errors: {errors.count}");
    return 0;
}
```

### 2. Pure DOD Game Physics with High-Speed Systems
```rust
component Position { x: f32, y: f32 }
component Velocity { vx: f32, vy: f32 }

system MovementSystem {
    query(mut pos: Position, vel: Velocity) {
        pos.x += vel.vx * 0.016;
        pos.y += vel.vy * 0.016;
    }
}

pipeline GamePipeline {
    stage Update {
        MovementSystem;
    }
}

fn main(): i32 {
    let mut world = ecs::create_world();
    
    // Spawn 100,000 entities in contiguous SoA memory
    for i in 0..100000 {
        world.spawn_with(
            Position(0.0, 0.0),
            Velocity(10.0, 5.0)
        );
    }

    GamePipeline(world);
    return 0;
}
```

---

## 📦 Quick Start & CLI Usage

### 1. Download & Unpack SDK
Extract the official `ecslang-sdk` package. Add the `bin/` directory to your system `PATH`.

### 2. Compile and Run
```bash
# Build and run directly (Debug mode)
ecslang run examples/01_hello_world.ecs

# High-Performance Release compilation (-O3 with LTO / MSVC link.exe)
ecslang build examples/olap_bigdata_analyzer.ecs --release -o olap_analyzer.exe

# Execute standalone bare-metal native binary
./olap_analyzer.exe
```

### 3. Compiler Options
```
ECS-Lang Native Compiler (LLVM 20 Backend)
Usage:
  ecslang build <file.ecs> [options]
  ecslang run   <file.ecs> [options]

Options:
  --release, -r   Enable maximum release optimizations (-O3, LTO)
  -O0 .. -O3      Specify LLVM optimization tier (default: -O0)
  -g, --debug     Generate PDB/CodeView debugging symbols
  --emit-ir       Dump LLVM IR (.ll) for inspection
  --emit-obj      Emit native object file (.obj)
  --target, -t    Cross-target triple (e.g., x86_64-pc-windows-msvc)
```

---

## 🏛️ Repository Architecture

```
ECSLang/
├── src/
│   ├── ECSLang.Core/           # AST nodes, Lexer tokens, SourceSpan, Diagnostics
│   ├── ECSLang.Frontend/       # Recursive-descent modular parser (Casts, Strings, BulkSpawn)
│   ├── ECSLang.Semantics/      # Type checker, Symbol table, Topological DAG analyzer
│   ├── ECSLang.Codegen.LLVM/   # LLVM IR 20 generator, SoA archetype memory emitter
│   ├── ECSLang.Toolchain/      # MSVC link.exe / lld-link toolchain auto-discovery
│   └── ECSLang.CLI/            # Native AOT command-line interface & phase profiler
├── std/                        # Standard Library (GUI, Network framing, Math, Keys)
├── examples/                   # Tested applications, benchmarks, and games
├── publish_sdk.bat             # Automated Native AOT SDK packager script
└── PLAN.md / ARCHITECTURE.md   # Complete technical specifications & design docs
```

---

## 📄 License

ECSLang is licensed under the **MIT License**. Free for commercial and non-commercial use.
Developed with extreme dedication to mechanical sympathy, low latency, and bare-metal computational efficiency.
