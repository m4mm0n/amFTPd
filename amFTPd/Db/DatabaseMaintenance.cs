/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-24
 *  Last Modified:  2025-11-24
 *  
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original
 *      author.
 * ====================================================================================================
 */

using amFTPd.Logging;

namespace amFTPd.Db
{
    /// <summary>
    /// Provides functionality for performing maintenance operations on a database,  such as consistency checks and
    /// backups.
    /// </summary>
    /// <remarks>This class is designed to encapsulate common database maintenance tasks,  including running
    /// file system consistency checks (FSCK) and creating backups.  It relies on a <see cref="DatabaseManager"/> for
    /// database operations and an  <see cref="IFtpLogger"/> for logging maintenance activity.</remarks>
    internal sealed class DatabaseMaintenance
    {
        private readonly DatabaseManager? _db;
        private readonly IFtpLogger _log;

        /// <summary>
        /// Initializes a new instance of the <see cref="DatabaseMaintenance"/> class.
        /// </summary>
        /// <param name="db">The <see cref="DatabaseManager"/> instance used to manage database operations.</param>
        /// <param name="log">The <see cref="IFtpLogger"/> instance used to log maintenance activities.</param>
        public DatabaseMaintenance(DatabaseManager? db, IFtpLogger log)
        {
            if (db != null) _db = db;
            _log = log;
        }
        /// <summary>
        /// Performs a file system consistency check (FSCK) on users, groups, and sections in the database.
        /// </summary>
        /// <remarks>This method verifies the integrity of the database by running consistency checks on
        /// users, groups, and sections. If the database is not initialized, the method exits without performing any
        /// operations.</remarks>
        public void RunFsck()
        {
            if (_db == null)
                return;

            _log.Log(FtpLogLevel.Info,"Running FSCK...");

            if (_db != null)
            {
                _db.FsckUsers();
                _db.FsckGroups();
                _db.FsckSections();
            } else
                _log.Log(FtpLogLevel.Error, "DatabaseManager was never initiated!");
        }
        /// <summary>
        /// Creates a backup of all databases.
        /// </summary>
        /// <remarks>This method logs the operation at the informational level and initiates the backup
        /// process for all databases. Ensure that the database system is properly configured to allow backups before
        /// calling this method.</remarks>
        public void CreateBackup()
        {
            _log.Log(FtpLogLevel.Info,"Creating DB backup...");
            if (_db != null) _db.BackupAll();
            else _log.Log(FtpLogLevel.Error, "DatabaseManager was never initiated!");
        }
    }
}
