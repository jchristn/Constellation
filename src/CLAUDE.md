# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and Development Commands

### Building the Solution
```bash
dotnet build Constellation.sln
dotnet build Constellation.sln -c Release
```

### Running Tests
```bash
dotnet run --project Test
dotnet run --project Test.Sqlite
```

### Building Individual Projects
```bash
dotnet build Constellation.Core/Constellation.Core.csproj
dotnet build Constellation.Controller/Constellation.Controller.csproj
dotnet build Constellation.ControllerServer/Constellation.ControllerServer.csproj
dotnet build Constellation.Worker/Constellation.Worker.csproj
```

### Running the Controller Server
```bash
dotnet run --project Constellation.ControllerServer
```

### Docker Build
```bash
build-docker.bat v1.0.0    # Windows
```

## Architecture Overview

Constellation is a RESTful workload placement and virtualization system designed for exactly-one resource ownership patterns. The architecture consists of:

### Core Components

1. **Constellation.Core** - Shared models, constants, and utilities
   - Contains base classes, settings, serialization, and common constants
   - Provides fundamental types like `WebsocketMessage`, `Settings`, and worker/controller base classes

2. **Constellation.Controller** - Base controller functionality 
   - Implements `ConstellationControllerBase` for routing requests to workers
   - Manages resource-to-worker mappings with automatic failover
   - Uses resource pinning: raw URL path becomes the resource key

3. **Constellation.ControllerServer** - Executable controller application
   - Complete controller implementation that can run standalone
   - Handles HTTP requests (port 8000) and WebSocket worker connections (port 8001)
   - Includes settings management and logging setup

4. **Constellation.Worker** - Base worker functionality
   - Implements `ConstellationWorkerBase` for worker implementations
   - Workers connect to controller via WebSocket and process requests
   - Must be extended to implement actual business logic

### Key Architectural Concepts

- **Resource Pinning**: The raw URL path (e.g., `/api/users`) becomes the resource key. All requests to the same URL are routed to the same worker for exclusive resource ownership.

- **Controller-Worker Pattern**: 
  - Controller receives HTTP requests and routes them to appropriate workers
  - Workers maintain exclusive ownership of assigned resources
  - Automatic failover when workers disconnect or fail health checks

- **WebSocket Communication**: Workers connect to controller via WebSocket for low-latency message passing

- **Health Monitoring**: Configurable heartbeat system (default: 2 second intervals, 3 max failures = 6 seconds until marked unhealthy)

### Project Dependencies

```
Constellation.ControllerServer
├── Constellation.Controller
│   └── Constellation.Core
└── Constellation.Core

Constellation.Worker
└── Constellation.Core

Test
├── Constellation.Controller
├── Constellation.Worker
└── RestWrapper (for HTTP testing)

Test.Sqlite
├── Constellation.Controller
├── Constellation.Worker
└── Microsoft.Data.Sqlite
```

### Default Configuration

- **HTTP Port**: 8000 (for client requests)
- **WebSocket Port**: 8001 (for worker connections) 
- **Settings File**: `./constellation.json`
- **Database File**: `./constellation.db` (if using SQLite features)
- **Log Files**: `./constellation.log` or `./logs/` directory

### Testing Strategy

The repository includes comprehensive integration tests in the `Test` project that demonstrate:
- Resource pinning behavior
- Worker failover scenarios  
- Load distribution across workers
- Concurrent request handling
- Worker recovery patterns

These tests create real controller and worker instances to validate the distributed system behavior.

## NuGet Packages

This solution produces three NuGet packages:
- `Constellation.Core` - Base classes and utilities
- `Constellation.Controller` - Controller functionality
- `Constellation.Worker` - Worker base classes

All packages target .NET 8.0 and include XML documentation.

## Code Style and Implementation Rules

These coding standards must be followed STRICTLY for consistency and maintainability:

### File Organization and Structure
- **One class or enum per file** - Never nest multiple classes or enums in a single file
- **Namespace first** - Namespace declaration at the top, using statements INSIDE the namespace block
- **Using statement order**:
  1. Microsoft and standard system library usings (alphabetical)
  2. Other using statements (alphabetical)

```csharp
namespace Constellation.Core
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using SyslogLogging;
    using Watson;
}
```

### Naming Conventions
- **Private class members** - Must start with underscore followed by PascalCase: `_FooBar` (not `_fooBar`)
- **No var keyword** - Always use explicit types: `string result` not `var result`
- **Avoid tuples** - Do not use tuples unless absolutely necessary

### Documentation Requirements
- **Public members only** - All public members, constructors, and methods MUST have XML documentation
- **No private documentation** - Do not document private members or methods
- **Include value constraints** - Document default values, min/max values, and their effects
- **Exception documentation** - Use `/// <exception>` tags to document exceptions public methods can throw
- **Nullability documentation** - Document null handling in XML comments
- **Thread safety** - Document thread safety guarantees in XML comments

```csharp
/// <summary>
/// Sets the timeout for HTTP requests in milliseconds.
/// </summary>
/// <value>Default: 30000ms, Minimum: 1000ms, Maximum: 300000ms</value>
/// <exception cref="ArgumentOutOfRangeException">Thrown when value is less than 1000 or greater than 300000</exception>
public int TimeoutMs 
{ 
    get { return _TimeoutMs; }
    set 
    {
        if (value < 1000 || value > 300000)
            throw new ArgumentOutOfRangeException(nameof(value), "Timeout must be between 1000ms and 300000ms");
        _TimeoutMs = value;
    }
}
```

### Property Implementation
- **Explicit getters/setters** - Use backing variables for properties requiring validation
- **Configurable defaults** - Avoid hardcoded constants; use configurable public members with reasonable defaults

### Async Programming
- **ConfigureAwait(false)** - Use on async calls where appropriate
- **CancellationToken parameters** - Every async method should accept CancellationToken unless class has CancellationToken/CancellationTokenSource member
- **Cancellation checks** - Check for cancellation at appropriate intervals in async methods
- **IEnumerable methods** - When implementing methods returning IEnumerable, also create async variants with CancellationToken

```csharp
public async Task<List<Worker>> GetWorkersAsync(CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();
    
    List<Worker> workers = await _Repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
    
    cancellationToken.ThrowIfCancellationRequested();
    return workers;
}
```

### Exception Handling
- **Specific exception types** - Use specific exceptions rather than generic Exception
- **Meaningful error messages** - Include context in error messages
- **Custom exceptions** - Consider domain-specific exception types
- **Exception filters** - Use when appropriate: `catch (SqlException ex) when (ex.Number == 2601)`

### Resource Management
- **IDisposable implementation** - Implement when holding unmanaged resources or disposable objects
- **Using statements** - Always use `using` statements/declarations for IDisposable objects
- **Full Dispose pattern** - Include protected virtual void Dispose(bool disposing)
- **Base.Dispose() calls** - Always call base.Dispose() in derived classes

### Nullable Reference Types and Input Validation
- **Enable nullable reference types** - Use `<Nullable>enable</Nullable>` in project files
- **Guard clauses** - Validate input parameters at method start
- **Null checks** - Use ArgumentNullException.ThrowIfNull() (.NET 6+) or manual null checks
- **Proactive null safety** - Eliminate situations where null might cause exceptions

```csharp
public void ProcessRequest(WebsocketMessage request, CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.Data);
    cancellationToken.ThrowIfCancellationRequested();
    
    // Implementation here
}
```

### Threading and Concurrency
- **Interlocked operations** - Use for simple atomic operations
- **ReaderWriterLockSlim** - Prefer over lock for read-heavy scenarios
- **Document thread safety** - Always document thread safety guarantees

### LINQ and Collections
- **Prefer LINQ when readable** - Use LINQ methods over manual loops when readability isn't compromised
- **Existence checks** - Use .Any() instead of .Count() > 0
- **Multiple enumeration awareness** - Consider .ToList() when needed to avoid multiple enumeration
- **Safe element access** - Use .FirstOrDefault() with null checks rather than .First()

### Class Design Principles
- **No assumptions** - Don't assume class members/methods exist on opaque classes - ask for implementation
- **SQL statement handling** - If manual SQL string preparation is used, assume there's a good architectural reason
- **README accuracy** - Analyze and ensure README accuracy when it exists

### Build and Quality Requirements
- **Compile without errors/warnings** - Ensure code compiles cleanly
- **Validation** - Test build commands and verify functionality