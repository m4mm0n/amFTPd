/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           UserStoreExtensions.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-07 09:35:51
 *  Last Modified:  2025-12-09 19:20:10
 *  CRC32:          0xC8D4120A
 *  
 *  Description:
 *      Best-effort enumeration of all users from an <see cref="IUserStore"/>. Works with InMemoryUserStore / BinaryUserStore...
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */







using amFTPd.Config.Ftpd;
using System.Reflection;

namespace amFTPd.Core.Site
{
    public static class UserStoreExtensions
    {
        /// <summary>
        /// Best-effort enumeration of all users from an <see cref="IUserStore"/>.
        /// Works with InMemoryUserStore / BinaryUserStore / BinaryUserStoreMmap
        /// by probing common fields/properties via reflection.
        /// Falls back to empty sequence if it can't see inside.
        /// </summary>
        public static IEnumerable<FtpUser> GetAllUsers(this IUserStore store)
        {
            if (store is null) throw new ArgumentNullException(nameof(store));
            var t = store.GetType();

            // 1) public or internal property "Users" : IEnumerable<FtpUser> or IDictionary<string,FtpUser>
            var prop = t.GetProperty("Users", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop is not null)
            {
                var value = prop.GetValue(store);
                if (value is IEnumerable<FtpUser> seq)
                    return seq;
                if (value is IDictionary<string, FtpUser> dictProp)
                    return dictProp.Values;
            }

            // 2) field "_users" : Dictionary<string,FtpUser>
            var field = t.GetField("_users", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is not null && field.GetValue(store) is IDictionary<string, FtpUser> dict)
                return dict.Values;

            // 3) method "GetAll" / "GetUsers" returning IEnumerable<FtpUser>
            var m = t.GetMethod("GetAll", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? t.GetMethod("GetUsers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m is not null && typeof(IEnumerable<FtpUser>).IsAssignableFrom(m.ReturnType))
            {
                var value = m.Invoke(store, null);
                if (value is IEnumerable<FtpUser> seq)
                    return seq;
            }

            // Last resort – we just can't see users in this backend.
            return Enumerable.Empty<FtpUser>();
        }
    }
}
