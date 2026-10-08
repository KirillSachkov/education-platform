global using CSharpFunctionalExtensions;
global using Microsoft.Extensions.Logging;
global using SharedKernel;

// Resolve CS0104 (IResult ambiguity) between CSharpFunctionalExtensions and
// ASP.NET Core. Connect/* endpoints return the ASP.NET Core variant; no file
// in this codebase uses CSharpFunctionalExtensions.IResult directly.
global using IResult = Microsoft.AspNetCore.Http.IResult;
