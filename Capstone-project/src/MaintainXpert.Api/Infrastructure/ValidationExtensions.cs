using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace MaintainXpert.Api.Infrastructure;

public static class ValidationExtensions
{
    // GetConstructors/GetProperties/GetCustomAttributes are pure functions of the request type,
    // so recomputing them on every request (the previous behavior) is avoidable work on the
    // hottest write path. The set of attributes per property never changes after the first
    // lookup for a given T, so it is safe to cache indefinitely per-process.
    private static readonly ConcurrentDictionary<Type, PropertyValidator[]> ValidatorCache = new();

    public static IResult? Validate<T>(T value) where T : notnull
    {
        var validators = ValidatorCache.GetOrAdd(typeof(T), BuildValidators);

        var errors = new Dictionary<string, List<string>>();

        foreach (var validator in validators)
        {
            var propertyValue = validator.Property.GetValue(value);

            foreach (var attribute in validator.Attributes)
            {
                if (attribute.IsValid(propertyValue))
                {
                    continue;
                }

                if (!errors.TryGetValue(validator.Property.Name, out var messages))
                {
                    messages = [];
                    errors[validator.Property.Name] = messages;
                }

                messages.Add(attribute.FormatErrorMessage(validator.Property.Name));
            }
        }

        if (errors.Count == 0)
        {
            return null;
        }

        return Results.ValidationProblem(
            errors.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray()));
    }

    private static PropertyValidator[] BuildValidators(Type type)
    {
        var primaryConstructor = type.GetConstructors()
            .OrderByDescending(constructor => constructor.GetParameters().Length)
            .FirstOrDefault();

        var validators = new List<PropertyValidator>();

        foreach (var property in type.GetProperties())
        {
            var attributes = property
                .GetCustomAttributes<ValidationAttribute>(inherit: true)
                .ToArray();

            if (attributes.Length == 0)
            {
                var matchingParameter = primaryConstructor?
                    .GetParameters()
                    .FirstOrDefault(parameter =>
                        string.Equals(parameter.Name, property.Name, StringComparison.OrdinalIgnoreCase));

                if (matchingParameter is not null)
                {
                    attributes = matchingParameter
                        .GetCustomAttributes<ValidationAttribute>(inherit: true)
                        .ToArray();
                }
            }

            if (attributes.Length == 0)
            {
                continue;
            }

            validators.Add(new PropertyValidator(property, attributes));
        }

        return validators.ToArray();
    }

    private sealed record PropertyValidator(PropertyInfo Property, ValidationAttribute[] Attributes);
}
