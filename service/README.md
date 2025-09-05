# Sync-to-Async POC

A proof-of-concept demonstrating how to serve async tasks that are manually resolved from external sources using `TaskCompletionSource` and manual `CancellationToken` management.

## Overview

This POC shows how to create tasks that are resolved from outside sources (third parties), which is particularly useful for scenarios such as:
- HTTP servers that delegate requests to message brokers and receive responses from queues
- Any I/O operations that are asynchronous but don't natively support Task Parallel Library (TPL)

## How It Works

The core component is the `SyncToAsync<TRequest, TResponse>` class that:

1. **Request Phase**: When a GET request is made to `/{req}`, it creates a `TaskCompletionSource` and stores it in a concurrent dictionary, then returns the associated task
2. **Resolution Phase**: When a POST request is made to the same `/{req}` endpoint with a response body, it resolves the corresponding task with the provided response
3. **Cancellation Management**: Properly handles cancellation tokens and application shutdown scenarios

## API Endpoints

- **GET `/{req}`** - Creates an async task and waits for resolution
- **POST `/{req}`** - Resolves the pending task with the request body as the response

## Key Features

- **Thread-safe**: Uses `ConcurrentDictionary` for managing inflight requests
- **Cancellation support**: Handles request cancellation and application shutdown
- **Generic implementation**: Works with any request/response types (constrained to non-null requests)
- **Logging**: Comprehensive logging for debugging and monitoring

## Example Usage

1. Start the application
2. Make a GET request to `/my-request` - this will hang waiting for resolution
3. In another client, POST to `/my-request` with response data
4. The GET request will complete with the POSTed data

```bash
# Terminal 1 - This will wait
curl http://localhost:5000/my-request

# Terminal 2 - This resolves the above request
curl -X POST http://localhost:5000/my-request -d "Hello World"

# Terminal 1 will now return "Hello World"
```

## Technical Details

- **Framework**: ASP.NET Core with .NET 8
- **Concurrency**: `ConcurrentDictionary` for thread-safe request tracking
- **Cancellation**: Integrated with ASP.NET Core cancellation tokens and application lifetime
- **Error Handling**: Returns 504 Gateway Timeout for cancelled requests

## Running the Application

```bash
dotnet run
```

The application will start on the default ASP.NET Core ports (typically http://localhost:5000 and https://localhost:5001).