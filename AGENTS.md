\## .NET Skill Library



You have access to a comprehensive .NET skill library. Use these skills for any C#/.NET development work. Always prefer skill-guided patterns over pre-training knowledge.



\### Skill Routing by Task Type



\*\*Writing C# Code:\*\*

\- `modern-csharp-coding-standards` - Records, pattern matching, immutability, value objects, async patterns

\- `csharp-concurrency-patterns` - Choosing between async/await, Channels, locks, synchronization primitives

\- `csharp-api-design` - API surface design, versioning, backward compatibility

\- `csharp-type-design-performance` - Sealed classes, readonly structs, Span<T>, Memory<T>



\*\*Entity Framework Core:\*\*

\- `efcore-patterns` - DbContext lifecycle, NoTracking, query splitting, migrations, interceptors

\- `database-performance` - N+1 prevention, read/write separation, query optimization



\*\*ASP.NET Core Web:\*\*

\- `middleware-patterns` - Pipeline ordering, custom middleware, exception handling

\- `razor-pages-patterns` - Page models, validation, anti-forgery, routing

\- `validation-patterns` - FluentValidation, DataAnnotations, custom validators

\- `exception-handling` - ProblemDetails, global handlers, error responses

\- `caching-strategies` - Output caching, Redis, HybridCache (.NET 9+)

\- `rate-limiting` - Request throttling, sliding window, concurrency limits

\- `security-headers` - CSP, HSTS, CORS, security middleware



\*\*Background Processing:\*\*

\- `background-services` - BackgroundService, IHostedService, outbox pattern, graceful shutdown

\- `dotnet-channels` - Producer/consumer, bounded channels, backpressure



\*\*Dependency Injection:\*\*

\- `microsoft-extensions-dependency-injection` - Service lifetimes, keyed services, factory patterns

\- `microsoft-extensions-configuration` - IOptions, configuration providers, secrets



\*\*Testing:\*\*

\- `dotnet-testing-strategy` - Test pyramid, unit vs integration decisions

\- `dotnet-xunit` - xUnit patterns, fixtures, theory data

\- `testcontainers` - Docker-based integration tests, database fixtures

\- `snapshot-testing` - Verify library, approval testing

\- `dotnet-playwright` - E2E browser testing

\- `crap-analysis` - CRAP scores, coverage analysis



\*\*Performance:\*\*

\- `dotnet-benchmarkdotnet` - Benchmark design, measurement methodology

\- `dotnet-performance-patterns` - Allocation reduction, GC optimization

\- `dotnet-profiling` - Profiler usage, hotspot identification

\- `dotnet-gc-memory` - GC modes, memory pressure, Large Object Heap



\*\*Native AOT:\*\*

\- `dotnet-native-aot` - AOT compilation, publishing, constraints

\- `dotnet-trimming` - Size optimization, linker configuration

\- `dotnet-aot-wasm` - WASM AOT with Blazor



\*\*Security:\*\*

\- `dotnet-security-owasp` - OWASP Top 10 for .NET

\- `dotnet-cryptography` - Encryption, hashing, key management

\- `dotnet-secrets-management` - Secret storage, Azure Key Vault, user secrets

\- `asp-net-core-identity-patterns` - Authentication, authorization, MFA



\*\*UI Frameworks:\*\*

\- `dotnet-blazor-patterns` - Server/WASM/Hybrid patterns

\- `dotnet-blazor-components` - Component lifecycle, rendering

\- `dotnet-maui-development` - Cross-platform mobile/desktop

\- `dotnet-winui` - Windows App SDK, WinUI 3

\- `razor-pages-patterns` - Server-side web UI



\*\*CI/CD:\*\*

\- `dotnet-gha-patterns` - GitHub Actions workflow patterns

\- `dotnet-gha-build-test` - Build/test matrix, caching

\- `dotnet-gha-publish` - NuGet, container publishing

\- `dotnet-ado-patterns` - Azure DevOps pipelines



\*\*Architecture:\*\*

\- `dotnet-architecture-patterns` - Clean architecture, vertical slice, modular monolith

\- `dotnet-solid-principles` - SOLID in practice

\- `dotnet-domain-modeling` - DDD patterns, aggregates

\- `project-structure` - Solution layout, Directory.Build.props



\*\*Deployment:\*\*

\- `fly-io` - Fly.io deployment, Machines, Volumes, networking

\- `dotnet-containers` - Docker for .NET

\- `dotnet-container-deployment` - Container orchestration



\*\*Specialized Frameworks:\*\*

\- `csharp-wolverinefx` - Messaging, HTTP services, Marten event sourcing

\- `aspire-configuration` - .NET Aspire AppHost configuration

\- `aspire-integration-testing` - Aspire testing patterns

\- `signalr-integration` - Real-time communication



\### Meta-Skills (Run After Changes)



\- `slopwatch` - Detect LLM-generated anti-patterns

\- `dotnet-agent-gotchas` - Common AI mistakes in .NET

\- `dotnet-build-analysis` - Build output analysis



\### Agent Activation



For complex domain-specific tasks, consider activating a specialist agent:

\- `dotnet-csharp-concurrency-specialist` - Race conditions, deadlocks, thread safety

\- `dotnet-security-reviewer` - Security audit, OWASP compliance

\- `dotnet-performance-analyst` - Profiling, benchmarking

\- `dotnet-blazor-specialist` - Blazor architecture

\- `dotnet-testing-specialist` - Test strategy design

