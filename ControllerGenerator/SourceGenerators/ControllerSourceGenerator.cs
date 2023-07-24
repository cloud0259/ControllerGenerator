using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using ControllerGenerator.Abstraction.Contracts;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections;

namespace ControllerGenerator.SourceGenerators
{

    [Generator]
    public sealed class ControllerSourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            //For debug
#if DEBUG
            if (!Debugger.IsAttached)
            {
                Debugger.Launch();
            }
#endif

            var compilationIncrementalValue = context.CompilationProvider;
            context.RegisterSourceOutput(
                compilationIncrementalValue,
                 (context, compilation) =>
                 {

                     var serviceTypes = GetServiceTypes(compilation);
                     // Generate controller source code for each type of service
                     foreach (var serviceType in serviceTypes)
                     {
                         var serviceMethods = GetServiceMethods(serviceType);
                         var sourceCode = GenerateControllerCode(serviceType, serviceMethods);
                         var sourceText = SourceText.From(sourceCode, Encoding.UTF8);
                         var sourcePath = $"{serviceType.Name}ControllerSourceGenerator.g.cs";
                         context.AddSource(sourcePath, sourceText);
                     }
                 });
        }

        private IEnumerable<ISymbol> GetSymbolRecursively(INamespaceSymbol symbol, INamedTypeSymbol serviceInterfaceSymbol, HashSet<ISymbol> uniqueSymbols = null)
        {
            if (uniqueSymbols == null)
            {
                uniqueSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            }

            var members = symbol.GetMembers();

            foreach (var member in members)
            {
                if (member.Kind != SymbolKind.Namespace) continue;

                if (member is ISymbol symbolMember)
                {
                    uniqueSymbols.Add(symbolMember);
                }
            }

            return uniqueSymbols;
        }


        /// <summary>
        /// Get the list of available services inheriting from the IAutoGenerateController interface
        /// </summary>
        /// <param name="compilation"></param>
        /// <returns></returns>
        private List<INamedTypeSymbol> GetServiceTypes(Compilation compilation)
        {
            var serviceInterfaceSymbol = compilation.GetTypeByMetadataName("ControllerGenerator.Abstraction.Contracts.IAutoGenerateController");

            string assemblyName = compilation.AssemblyName;
            int lastDotIndex = assemblyName.IndexOf('.');
            string projectName = assemblyName.Contains(".") ? assemblyName.Substring(0, lastDotIndex) : assemblyName;

            IEnumerable<IAssemblySymbol> assemblySymbols = compilation.SourceModule.ReferencedAssemblySymbols.Where(q => q.Name.Contains(projectName));

            List<INamedTypeSymbol> servicesTypes = new List<INamedTypeSymbol>();
            foreach (var assemblySymbol in assemblySymbols)
            {
                var namespaceSymbols = assemblySymbol.GlobalNamespace.GetNamespaceMembers()
              .First(m => m.Name == projectName);

                if (namespaceSymbols != null)
                {
                    var services = GetSymbolRecursively(namespaceSymbols, serviceInterfaceSymbol);
                    foreach (var service in services)
                    {
                        if (services != null)
                        {
                            var serviceInterfaces = GetInterfacesFromSymbol(service, serviceInterfaceSymbol);

                            if (serviceInterfaces != null)
                            {
                                servicesTypes.AddRange(serviceInterfaces.ToList());
                            }
                        }
                    }
                }
            }

            servicesTypes.AddRange(compilation.SyntaxTrees
               .SelectMany(syntaxTree => syntaxTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
               .Select(classDeclaration => compilation.GetSemanticModel(classDeclaration.SyntaxTree).GetDeclaredSymbol(classDeclaration))
               .OfType<INamedTypeSymbol>()
               .Where(typeSymbol => typeSymbol.AllInterfaces.Contains(serviceInterfaceSymbol))
               .ToList());

            return GetDistinctServices(servicesTypes);
        }

        private List<INamedTypeSymbol> GetDistinctServices(IEnumerable<ISymbol> servicesTypes)
        {
            var uniqueServices = new List<INamedTypeSymbol>();
            var uniqueSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

            foreach (var service in servicesTypes)
            {
                if (service is INamedTypeSymbol namedTypeSymbol && uniqueSymbols.Add(service))
                {
                    uniqueServices.Add(namedTypeSymbol);
                }
            }

            return uniqueServices;
        }

        private IEnumerable<INamedTypeSymbol> GetInterfacesFromSymbol(ISymbol symbol, INamedTypeSymbol serviceInterfaceSymbol)
        {
            var interfaces = Enumerable.Empty<INamedTypeSymbol>();

            if (symbol is INamespaceOrTypeSymbol namespaceOrTypeSymbol)
            {
                interfaces = namespaceOrTypeSymbol.GetMembers()
                    .SelectMany(member => GetInterfacesFromSymbol(member, serviceInterfaceSymbol))
                    .Concat(namespaceOrTypeSymbol.GetMembers()
                        .Where(member => member.Kind == SymbolKind.NamedType && ((INamedTypeSymbol)member).TypeKind == TypeKind.Class)
                        .OfType<INamedTypeSymbol>()
                        .Where(typeSymbol => typeSymbol.AllInterfaces.Contains(serviceInterfaceSymbol)));
            }

            return interfaces;
        }


        /// <summary>
        /// Get the list of methods available from the service
        /// </summary>
        /// <param name="serviceType"></param>
        /// <returns></returns>
        private List<MethodDeclarationSyntax> GetServiceMethods(INamedTypeSymbol serviceType)
        {
            var methods = serviceType
                .GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m =>
                    m.DeclaredAccessibility == Accessibility.Public &&
                    m.MethodKind == MethodKind.Ordinary &&
                    !m.IsStatic)
                .ToList();

            var methodDeclarations = new List<MethodDeclarationSyntax>();

            foreach (var methodSymbol in methods)
            {
                var methodName = methodSymbol.Name;


                var returnType = methodSymbol.ReturnType.ToString().ToLower();

                var parameters = methodSymbol.Parameters;
                var parametersSignature = string.Join(", ", parameters.Select(p =>
                    $"{p.Type.ToString()} {p.Name}"
                ));

                var attributeList = new List<AttributeSyntax>();

                // Récupère les attributs du symbole de méthode
                foreach (var attributeData in methodSymbol.GetAttributes())
                {
                    // Récupère le nom complet de l'attribut, y compris le nom de l'espace de noms
                    var attributeNamespace = attributeData.AttributeClass.ContainingNamespace.ToDisplayString();
                    var attributeName = attributeData.AttributeClass.Name;
                    if (attributeName.EndsWith("Attribute", StringComparison.OrdinalIgnoreCase))
                    {
                        attributeName = attributeName.Substring(0, attributeName.Length - "Attribute".Length);
                    }

                    var arguments = new List<AttributeArgumentSyntax>();

                    foreach (var argument in attributeData.ConstructorArguments)
                    {
                        ExpressionSyntax argumentExpression = null;
                        switch (argument.Value)
                        {
                            case int intValue:
                                argumentExpression = SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(intValue));
                                break;
                            case string stringValue:
                                argumentExpression = SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(stringValue));
                                break;
                            case bool boolValue:
                                argumentExpression = SyntaxFactory.LiteralExpression(boolValue ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression);
                                break;
                            case double doubleValue:
                                argumentExpression = SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(doubleValue));
                                break;
                            case float floatValue:
                                argumentExpression = SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(floatValue));
                                break;
                            case decimal decimalValue:
                                argumentExpression = SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(decimalValue));
                                break;
                            case DateTime dateTimeValue:
                                argumentExpression = SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(dateTimeValue.ToString("o")));
                                break;
                            case TimeSpan timeSpanValue:
                                argumentExpression = SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(timeSpanValue.ToString()));
                                break;
                            default:
                                //argumentExpression = SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
                                break;
                        }
                        if (argumentExpression != null)
                        {
                            arguments.Add(SyntaxFactory.AttributeArgument(argumentExpression));
                        }
                    }

                    var attributeSyntax = SyntaxFactory.Attribute(SyntaxFactory.ParseName($"{attributeNamespace}.{attributeName}"));
                    if (arguments.Count > 0)
                    {
                        attributeSyntax = attributeSyntax.WithArgumentList(SyntaxFactory.AttributeArgumentList(SyntaxFactory.SeparatedList(arguments)));
                    }

                    // Ajoute l'attribut à la liste
                    attributeList.Add(attributeSyntax);
                }

                var methodDeclaration = SyntaxFactory.MethodDeclaration(
                        SyntaxFactory.ParseTypeName(returnType),
                        methodName
                    )
                    .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                    .WithParameterList(SyntaxFactory.ParseParameterList($"({parametersSignature})"))
                    .WithAttributeLists(SyntaxFactory.List(new[] { SyntaxFactory.AttributeList(SyntaxFactory.SeparatedList(attributeList)) }));

                methodDeclarations.Add(methodDeclaration);

            }

            return methodDeclarations;
        }

        /// <summary>
        /// Generate the controller code from the service
        /// </summary>
        /// <param name="serviceType"></param>
        /// <param name="serviceMethods"></param>
        /// <returns></returns>
        private string GenerateControllerCode(INamedTypeSymbol serviceType, List<MethodDeclarationSyntax> serviceMethods)
        {
            var stringBuilder = new StringBuilder();
            var serviceName = serviceType.Name.IndexOf("appservice", StringComparison.OrdinalIgnoreCase) >= 0 ? Regex.Replace(serviceType.Name, "appservice", "", RegexOptions.IgnoreCase) : serviceType.Name;
            var serviceContract = serviceType.Interfaces.Count() > 0 && serviceType.Interfaces.Where(i => i.Name.Contains(serviceType.Name)).Count() > 0 ? serviceType.Interfaces.Where(i => i.Name.Contains(serviceType.Name)).FirstOrDefault()?.Name : serviceType.Name;

            var serviceNamespace = serviceType.Interfaces.Where(i => i.Name.Contains(serviceType.Name)).FirstOrDefault()?.OriginalDefinition.ContainingNamespace.ToString() ?? serviceType.ContainingNamespace.ToString();
            var serviceRootNamespace = serviceNamespace.Substring(0, serviceNamespace.LastIndexOf('.'));

            stringBuilder.AppendLine("using Microsoft.AspNetCore.Mvc;");
            stringBuilder.AppendLine("using System;");
            stringBuilder.AppendLine("using System.Threading.Tasks;");
            stringBuilder.AppendLine($"using {serviceNamespace};");

            stringBuilder.AppendLine($"namespace {serviceRootNamespace}.Controllers.{serviceName}");
            stringBuilder.AppendLine("{");
            stringBuilder.AppendLine();

            stringBuilder.AppendLine("    [Route(\"api/[controller]\")]");
            stringBuilder.AppendLine("    [ApiController]");
            stringBuilder.AppendLine($"    public class {serviceName}Controller : ControllerBase");
            stringBuilder.AppendLine("    {");
            stringBuilder.AppendLine();

            stringBuilder.AppendLine($"        private readonly {serviceContract} _service;");
            stringBuilder.AppendLine();

            stringBuilder.AppendLine($"        public {serviceName}Controller({serviceContract} service)");
            stringBuilder.AppendLine("        {");
            stringBuilder.AppendLine("        _service = service;");
            stringBuilder.AppendLine("        }");
            stringBuilder.AppendLine();


            foreach (var method in serviceMethods)
            {
                if (method != null)
                {
                    var methodName = method.Identifier.ValueText.IndexOf("async", StringComparison.OrdinalIgnoreCase) >= 0 ? Regex.Replace(method.Identifier.ValueText, "async", "", RegexOptions.IgnoreCase) : method.Identifier.ValueText;
                    var returnType = method.ReturnType.ToString().ToLower();

                    var parameters = method.ParameterList.Parameters;
                    var parametersSignature = string.Join(", ", parameters.Select(p =>
                        $"{p.Type.ToString()} {p.Identifier.ValueText}"
                    ));
                    var isHttpAttribute = false;
                    foreach (var attributeList in method.AttributeLists)
                    {
                        foreach (var attribute in attributeList.Attributes)
                        {
                            string attributeName = attribute.Name.ToString();
                            string attributeArguments = string.Join(", ", attribute.ArgumentList?.Arguments.Select(a => a.ToString()) ?? Enumerable.Empty<string>());

                            if (!attributeName.StartsWith("System"))
                            {
                                stringBuilder.AppendLine($"        [{attributeName}({attributeArguments})]");
                                if (attributeName.Contains("Microsoft.AspNetCore.Mvc.Http"))
                                {
                                    isHttpAttribute = true;
                                }
                            }
                        }
                    }

                    if (!isHttpAttribute)
                    {
                        switch (methodName.ToLower())
                        {
                            case var name when name.Contains("get"):
                                stringBuilder.AppendLine($"        [HttpGet(\"{methodName}\")]");
                                break;
                            case var name when name.Contains("update"):
                                stringBuilder.AppendLine($"        [HttpPut(\"{methodName}\")]");
                                break;
                            case var name when name.Contains("delete"):
                                stringBuilder.AppendLine($"        [HttpDelete(\"{methodName}\")]");
                                break;
                            case var name when name.Contains("patch"):
                                stringBuilder.AppendLine($"        [HttpPatch(\"{methodName}\")]");
                                break;
                            default:
                                stringBuilder.AppendLine($"        [HttpPost(\"{methodName}\")]");
                                break;
                        }
                    }

                    if (returnType != "void")
                    {
                        stringBuilder.AppendLine($"        public async Task<IActionResult> {methodName}({parametersSignature})");
                    }
                    else
                    {
                        stringBuilder.AppendLine($"        public IActionResult {methodName}({parametersSignature})");
                    }
                    stringBuilder.AppendLine("        {");

                    var arguments = string.Join(", ", parameters.Select(p => p.Identifier.ValueText));

                    if (returnType != "void" && returnType != "system.threading.tasks.task")
                    {
                        stringBuilder.AppendLine($"            var result = await _service.{method.Identifier.ValueText}({arguments});");
                        stringBuilder.AppendLine($"            return Ok(result);");
                    }
                    else
                    {
                        var awaitText = returnType == "void" ? string.Empty : "await";
                        stringBuilder.AppendLine($"            {awaitText} _service.{method.Identifier.ValueText}({arguments});");
                        stringBuilder.AppendLine($"            return Ok();");
                    }

                    stringBuilder.AppendLine("        }");
                    stringBuilder.AppendLine();
                }
            }

            stringBuilder.AppendLine("    }");
            stringBuilder.AppendLine("}");

            return stringBuilder.ToString();
        }
    }
}