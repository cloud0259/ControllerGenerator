# ControllerGenerator
| ControllerGenerator | ControllerGenerator.Abstraction | Action |
|------------------|------------------|------------------|
| [![NuGet](https://img.shields.io/nuget/v/ControllerGenerator?label=ControllerGenerator)](https://www.nuget.org/packages/ControllerGenerator)       | [![NuGet](https://img.shields.io/nuget/v/ControllerGenerator.Abstraction?label=ControllerGenerator.Abstraction)](https://www.nuget.org/packages/ControllerGenerator.Abstraction)         | [![Mon Badge](https://github.com/cloud0259/ControllerGenerator/workflows/.build/badge.svg)](https://github.com/cloud0259/ControllerGenerator/actions)         |
| ![Nuget](https://img.shields.io/nuget/dt/ControllerGenerator?label=ControllerGenerator)         | ![Nuget](https://img.shields.io/nuget/dt/ControllerGenerator.Abstraction?label=ControllerGenerator.Abstraction)         |          |


This project is an automatic controller generator for .NET projects. It uses Source Generator to automatically create controllers from specified services in a project containing the services.

## Installation
To use this generator, you need to install the ControllerGenerator.Abstractions library in the project that contains the services you want to use for generating controllers.

```
Install-Package ControllerGenerator.Abstractions
```
In your web project, it is necessary to install the ControllerGenerator library
```
Install-Package ControllerGenerator
```

## Usage
To have your services recognized by the generator, each service or its interface must inherit the IAutoGenerateController interface.

```csharp
public interface IMyService : IAutoGenerateController
{
    // Service methods
}
```
## Request Generation based on Method Prefixes

Requests in the generated controllers are determined based on the method prefixes:

- If the service method starts with "Get", "Post", "Patch", "Update", "Create", or "Delete", the generated request will correspond to the respective HTTP methods.
- If there is no prefix, the generated request will be of type HTTP POST.

Attributes can be used in services. These will be recognized by the Source Generator

## Requirements
The Source Generator library must be installed in the target project. The target project should be a .NET 7.0 web application or a higher version.

### Example
Here's an example of using this generator:

```csharp
// In the project containing the services

public interface IMyService : IAutoGenerateController
{
    Task<User> GetUserAsync(int id);
    Task<User> PostUser(UserData data);
    Task UpdateUser(int id, UserData data);
    // Other service methods
}

//Service implementation
public class MyService : IMyService
{
    //Property, Ctor ....
    
    [HttpGet] //it's not mandatory
    [Authorize("Admin")]
    public async Task<User> GetUserAsync(int id)
    {
        return await userList.FirstOrDefaultAsync(x => x.Id == id);
    }

    [NoGenerated] //This method is not generate for the controller
    public Task<User> PostUser(UserData data)
    {
        throw new Exception("An error has occured")
    }

    //Other method...
}

// In the .NET 7.0 or higher web project

// The generator will automatically create a controller for IMyService with corresponding HTTP methods for each method in the service.
public class MyController : ControllerBase
{
    private readonly IMyService _myService;

    public MyController(IMyService myService)
    {
        _myService = myService;
    }

    // GET endpoint generated for GetUserAsync
    [HttpGet("{id}")]
    public IActionResult GetUser(int id)
    {
        // Implementation
    }

    // POST endpoint generated for PostUser
    [HttpPost]
    public IActionResult PostUser([FromBody] UserData data)
    {
        // Implementation
    }

    // Other endpoints for UpdateUser and other service methods
}
```

It is possible to replace a controller generated by the source generator by creating a controller that inherits the interface of the service to replace

```csharp
public class MyController : ControllerBase, IMyService
{
    //Implement the interface to replace the self-generated controller
}
```

## Disclaimer
This generator uses Source Generator to automatically generate controllers. Make sure you understand how it works before using it in your project. Please perform thorough testing to ensure that the generated controllers meet your requirements.

## Contributing
Contributions are welcome! Please see the CONTRIBUTING.md file for information on how to contribute to this project.

## License
This project is licensed under the MIT License.
