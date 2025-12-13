/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           CoreExtensions.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-01 05:44:45
 *  Last Modified:  2025-12-13 04:25:57
 *  CRC32:          0x3417888C
 *  
 *  Description:
 *      Determines whether the specified flag character is present in the set, using a case-insensitive comparison.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */




using amFTPd.Config.Ftpd;
using System.Collections;
using System.Collections.Immutable;
using System.Reflection;

namespace amFTPd.Utils
{
    internal static class CoreExtensions
    {
        /// <summary>
        /// Determines whether the specified flag character is present in the set, using a case-insensitive comparison.
        /// </summary>
        /// <remarks>The comparison treats uppercase and lowercase versions of the flag character as
        /// equivalent. For example, both 'A' and 'a' are considered the same flag.</remarks>
        /// <param name="flags">The set of flag characters to search.</param>
        /// <param name="flag">The flag character to locate in the set. The comparison is case-insensitive.</param>
        /// <returns><see langword="true"/> if the set contains the specified flag character (ignoring case); otherwise, <see
        /// langword="false"/>.</returns>
        public static bool HasFlag(this ImmutableHashSet<char> flags, char flag)
            => flags.Contains(char.ToUpperInvariant(flag));

        /// <summary>
        /// Returns true if the instance itself is null, or if ANY of its
        /// public instance fields/properties are considered "empty".
        /// </summary>
        public static bool HasAnyEmptyPublicMember<T>(this T? instance)
        {
            if (instance == null)
                return true;

            var type = instance!.GetType();

            // Public instance properties with getters
            var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                                 .Where(p => p.GetMethod != null && p.GetMethod.GetParameters().Length == 0);

            if ((from prop in properties
                    let value = prop.GetValue(instance)
                    where IsEmpty(value, prop.PropertyType)
                    select prop).Any())
                return true;

            // Public instance fields
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            return (from field in fields let value = field.GetValue(instance) where IsEmpty(value, field.FieldType) select field).Any();
        }

        public static void ValidateRequired_BUS_BuildRecord(this FtpUser? u)
        {
            if (string.IsNullOrWhiteSpace(u?.UserName))
                throw new ArgumentException("UserName is required.");

            if (string.IsNullOrWhiteSpace(u?.PasswordHash))
                throw new ArgumentException("PasswordHash is required.");

            if (string.IsNullOrWhiteSpace(u?.HomeDir))
                throw new ArgumentException("HomeDir is required.");
        }

        private static bool IsEmpty(object? value, Type type)
        {
            // Null is always empty
            if (value == null)
                return true;

            // Strings: null or whitespace
            if (type == typeof(string))
                return string.IsNullOrWhiteSpace((string)value);

            // Nullable<T>: if HasValue=false it was caught by value==null above,
            // so here we just unwrap and check the underlying type if you want.
            var underlyingNullable = Nullable.GetUnderlyingType(type);
            if (underlyingNullable != null)
            {
                // value is a boxed Nullable<T> that has a value here
                var hasValueProp = type.GetProperty("HasValue");
                var valueProp = type.GetProperty("Value");
                if (hasValueProp != null && valueProp != null)
                {
                    var hasValue = (bool)hasValueProp.GetValue(value)!;
                    if (!hasValue)
                        return true; // should already be null, but just in case

                    var inner = valueProp.GetValue(value);
                    return IsEmpty(inner, underlyingNullable);
                }
            }

            // Collections: empty if Count == 0 or no elements
            if (value is ICollection collection)
                return collection.Count == 0;

            if (value is IEnumerable enumerable && value is not string)
                // check if it has any element
                return !enumerable.Cast<object?>().Any();

            // Value types: treat default(T) as empty
            if (type.IsValueType)
            {
                var defaultValue = Activator.CreateInstance(type);
                return value.Equals(defaultValue);
            }

            // For other reference types, we only consider null as empty,
            // and we've already handled that above.
            return false;
        }
    }
}
