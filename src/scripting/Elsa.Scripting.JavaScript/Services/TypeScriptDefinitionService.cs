﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper.Internal;
using Elsa.Models;
using Elsa.Scripting.JavaScript.Events;
using MediatR;

namespace Elsa.Scripting.JavaScript.Services
{
    public class TypeScriptDefinitionService : ITypeScriptDefinitionService
    {
        private readonly IEnumerable<ITypeDefinitionProvider> _providers;
        private readonly IMediator _mediator;

        public TypeScriptDefinitionService(IEnumerable<ITypeDefinitionProvider> providers, IMediator mediator)
        {
            _providers = providers;
            _mediator = mediator;
        }

        public async Task<string> GenerateTypeScriptDefinitionsAsync(WorkflowDefinition? workflowDefinition = default, string? context = default, CancellationToken cancellationToken = default)
        {
            var builder = new StringBuilder();
            var providerContext = new TypeDefinitionContext(workflowDefinition, context);
            var types = await CollectTypesAsync(providerContext, cancellationToken);
            
            // Render type declarations for anything except those listed in TypeConverters.
            foreach (var type in types)
            {
                var shouldRenderDeclaration = ShouldRenderTypeDeclaration(providerContext, type);

                if (shouldRenderDeclaration)
                    RenderTypeDeclaration(providerContext, type, types, builder);
            }
            
            string GetTypeScriptTypeInternal(Type type) => GetTypeScriptType(providerContext, type, types);
            var renderingTypeScriptDefinitions = new RenderingTypeScriptDefinitions(workflowDefinition, GetTypeScriptTypeInternal, context, builder);
            await _mediator.Publish(renderingTypeScriptDefinitions, cancellationToken);

            return builder.ToString();
        }

        private async Task<ISet<Type>> CollectTypesAsync(TypeDefinitionContext context, CancellationToken cancellationToken = default)
        {
            var collectedTypes = new HashSet<Type>();

            foreach (var provider in _providers)
            {
                var providedTypes = await provider.CollectTypesAsync(context, cancellationToken);

                foreach (var providedType in providedTypes) 
                    CollectType(providedType, collectedTypes);
            }
            
            return collectedTypes;
        }

        private static void CollectType(Type type, ISet<Type> collectedTypes)
        {
            if (type.IsNullableType())
            {
                CollectType(type.GetTypeOfNullable(), collectedTypes);
                return;
            }

            if (collectedTypes.Contains(type))
                return;

            collectedTypes.Add(type);

            // Collect generic type argument types.
            foreach (var typeArgType in type.GenericTypeArguments.Where(x => !collectedTypes.Contains(x)))
                CollectType(typeArgType, collectedTypes);

            // Collect property types.
            var propertyTypes = type.GetProperties().Select(x => x.PropertyType).Where(x => !collectedTypes.Contains(x));

            foreach (var propertyType in propertyTypes)
                CollectType(propertyType, collectedTypes);

            // Collect method return and argument types.
            var methods = type.GetMethods(BindingFlags.Public);

            foreach (var method in methods)
            {
                var returnType = method.ReturnType;

                if (returnType != typeof(void))
                    CollectType(returnType, collectedTypes);

                var argTypes = method.GetParameters().Select(x => x.ParameterType).ToList();

                foreach (var argType in argTypes)
                    CollectType(argType, collectedTypes);
            }
        }

        private void RenderTypeDeclaration(TypeDefinitionContext context, Type type, ISet<Type> collectedTypes, StringBuilder output) => RenderTypeDeclaration(context, "interface", type, collectedTypes, output);

        private void RenderTypeDeclaration(TypeDefinitionContext context, string symbol, Type type, ISet<Type> collectedTypes, StringBuilder output)
        {
            var typeName = GetSafeSymbol(type.Name);
            var properties = type.GetProperties();
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Where(x => !x.IsSpecialName).ToList();

            output.AppendLine($"declare {symbol} {typeName} {{");

            foreach (var property in properties)
            {
                var typeScriptType = GetTypeScriptType(context, property.PropertyType, collectedTypes);
                var propertyName = property.PropertyType.IsNullableType() ? $"{property.Name}?" : property.Name;
                output.AppendLine($"{propertyName}: {typeScriptType};");
            }

            foreach (var method in methods)
            {
                if(method.Name.StartsWith("<"))
                    continue;

                if(symbol == "interface" && method.IsStatic)
                    continue;

                if (method.IsStatic)
                    output.Append("static ");

                output.Append($"{method.Name}(");

                var arguments = method.GetParameters().Select(x => $"{x.Name}:{GetTypeScriptType(context, x.ParameterType, collectedTypes)}");
                output.Append(string.Join(", ", arguments));
                output.Append(")");

                var returnType = method.ReturnType;
                if (returnType != typeof(void))
                    output.AppendFormat(":{0}", GetTypeScriptType(context, returnType, collectedTypes));

                output.AppendLine(";");
            }

            output.AppendLine("}");
        }

        private string GetTypeScriptType(TypeDefinitionContext context, Type type, ISet<Type> collectedTypes)
        {
            if (type.IsNullableType())
                type = type.GetTypeOfNullable();

            var provider = _providers.FirstOrDefault(x => x.SupportsType(context, type));
            var typeScriptType = provider != null ? provider.GetTypeDefinition(context, type) : collectedTypes.Contains(type) ? type.Name : "any";
            return GetSafeSymbol(typeScriptType);
        }

        private bool ShouldRenderTypeDeclaration(TypeDefinitionContext context, Type type)
        {
            var provider = _providers.FirstOrDefault(x => x.SupportsType(context, type));
            return provider == null;
        }

        private string GetSafeSymbol(string symbol) => symbol.Replace("`", "");
    }
}