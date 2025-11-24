using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Klinkby.Booqr.Infrastructure.Generators;

[Generator]
[ExcludeFromCodeCoverage]
public sealed class QueryFieldsGenerator : IIncrementalGenerator
{
    private const string Namespace = "Klinkby.Booqr.Infrastructure.Repositories";
    private const string AttributeClassName = "QueryFieldsAttribute";
    private const string AttributeFullName = $"{Namespace}.{AttributeClassName}";
    private const string FieldsName = "Fields";
    private const string AttributeSource = $$"""
                                             using System;
                                             using System.CodeDom.Compiler;

                                             namespace {{Namespace}}
                                             {
                                                 [GeneratedCode("{{Namespace}}.Generators", "1.0")]
                                                 [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
                                                 public sealed class {{AttributeClassName}}(params string[] fields) : Attribute
                                                 {
                                                     public string[] {{FieldsName}} = fields;
                                                 }
                                             }
                                             """;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(ctx => ctx.AddSource($"{AttributeClassName}.g.cs", AttributeSource));
        IncrementalValuesProvider<GeneratorAttributeSyntaxContext> classesMarkedWithTheGeneratorAttribute = context
            .SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeFullName,
                (node, _) => node is ClassDeclarationSyntax
                {
                    AttributeLists.Count: > 0
                }, // or RecordDeclarationSyntax { AttributeLists.Count: > 0 },
                (ctx, _) => ctx
            );
        context.RegisterSourceOutput(
            classesMarkedWithTheGeneratorAttribute.Combine(context.CompilationProvider),
            GenerateClass);
    }

    private static void GenerateClass(SourceProductionContext ctx,
        (GeneratorAttributeSyntaxContext AttributeContext, Compilation Compilation) symbol)
    {
        AttributeData attribute =
            symbol.AttributeContext.Attributes.First(x => AttributeClassName == x.AttributeClass?.MetadataName);
        var fieldNames = attribute.ConstructorArguments[0].Values.Select(x => (string)(x.Value ?? "")).ToArray();
        if (fieldNames.Length == 0)
        {
            return;
        }
        ClassDeclarationSyntax @class = symbol.AttributeContext.TargetNode.AncestorsAndSelf()
            .OfType<ClassDeclarationSyntax>().First();
        var className = @class.Identifier.Text;
        var commaSeparated = string.Join(",", fieldNames);
        var parametersCommaSeparated = "@" + string.Join(",@", fieldNames);
        var parametersAssignment = string.Join(",", fieldNames.Select(field => $"{field} = @{field}"));
        var coalesceParametersAssignment = string.Join(",",
            fieldNames.Select(field => $"{field} = COALESCE(@{field},{field})"));
        var coalesceParameterValues = string.Join(",", fieldNames.Select(field => $"COALESCE(@{field},{field})"));

        var generatedCode = $$"""
                              namespace {{Namespace}}
                              {
                                  partial class {{className}} {
                                      private const string CommaSeparated = "{{commaSeparated}}";
                                      private const string ParametersCommaSeparated = "{{parametersCommaSeparated}}";
                                      private const string ParametersAssignment = "{{parametersAssignment}}";
                                      private const string CoalesceParametersAssignment = "{{coalesceParametersAssignment}}";
                                      private const string CoalesceValues = "{{coalesceParameterValues}}";

                                      private const string GetAllQuery = $"SELECT id,{CommaSeparated},created,modified,deleted FROM {TableName} WHERE deleted IS NULL LIMIT @Num OFFSET @Start";
                                      private const string GetByIdQuery = $"SELECT id,{CommaSeparated},created,modified,deleted FROM {TableName} WHERE deleted IS NULL and id = @Id";
                                      private const string InsertQuery = $"INSERT INTO {TableName} ({CommaSeparated},created,modified,deleted) VALUES ({ParametersCommaSeparated},@Created,@Modified,@Deleted) RETURNING id";
                                      private const string UpdateQuery = $"UPDATE {TableName} SET {ParametersAssignment},modified = @Modified WHERE deleted is NULL AND id = @Id AND (@Version IS NULL OR modified = @Version)";
                                      private const string PatchQuery = $"UPDATE {TableName} SET {CoalesceParametersAssignment},modified = @Modified WHERE deleted is NULL AND id = @Id AND (@Version IS NULL OR modified = @Version) AND ({CommaSeparated}) IS DISTINCT FROM ({CoalesceValues})" +
                                      private const string DeleteQuery = $"UPDATE {TableName} SET deleted = @Now WHERE id = @Id AND deleted IS NULL";
                                      private const string UndeleteQuery = $"UPDATE {TableName} SET deleted = NULL WHERE id = @Id AND deleted IS NOT NULL";
                                  }
                              }
                              """;
        ctx.AddSource($"{className}.g.cs", generatedCode);
    }
}
