﻿#if NETSTANDARD
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SharpCompress.Compressors.Xz;
using SharpCompress.Readers.Tar;

namespace Knapcode.TorSharp.Tools
{
    internal partial class ArchiveUtility
    {
        public static async Task TestDebAsync(string debPath)
        {
            await ReadDebAsync(
                debPath,
                outputDir: null,
                getEntryPath: null,
                shouldExtract: false).ConfigureAwait(false);
        }

        public static async Task ExtractDebAsync(
            string debPath,
            string outputDir,
            Func<string, string> getEntryPath)
        {
            await ReadDebAsync(
                debPath,
                outputDir,
                getEntryPath,
                shouldExtract: true).ConfigureAwait(false);
        }

        private static async Task ReadDebAsync(
            string debPath,
            string outputDir,
            Func<string, string> getEntryPath,
            bool shouldExtract)
        {
            // Debian packages (.deb) are "ar" archives with three files.
            //
            // 8 bytes for signature (should be "!<arch>\n")
            //
            // Each section has the following header:
            //  - 16 bytes for file identifier (ASCII)
            //  - 12 bytes for file modification timestamp (decimal)
            //  - 6 bytes for owner ID (decimal)
            //  - 6 bytes for group ID (decimal)
            //  - 8 bytes for file mode (octal)
            //  - 10 bytes for file size (decimal)
            //  - 2 bytes for ending characters ("`\n")
            //
            // The three sections are:
            //  - Package Section
            //  - Control Section
            //  - Data Section
            //
            // We only care about the data section and we expect it to be a .tar.xz.
            const int signatureLength = 8;
            const int headerLength = 16 + 12 + 6 + 6 + 8 + 10 + 2;
            var buffer = new byte[headerLength];
            using (var fileStream = new FileStream(debPath, FileMode.Open))
            {
                var read = fileStream.Read(buffer, 0, signatureLength);
                if (read != signatureLength || Encoding.ASCII.GetString(buffer, 0, read) != "!<arch>\n")
                {
                    throw new TorSharpException("The Debian package did not have the expected file signature.");
                }

                read = fileStream.Read(buffer, 0, headerLength);
                var packageSectionHeader = ArFileHeader.Read(buffer);
                if (read != headerLength
                    || packageSectionHeader.FileSize != 4)
                {
                    throw new TorSharpException("The Debian package did not have the expected package section file header.");
                }
                fileStream.Position += packageSectionHeader.FileSize;

                read = fileStream.Read(buffer, 0, headerLength);
                var controlSectionHeader = ArFileHeader.Read(buffer);
                fileStream.Position += controlSectionHeader.FileSize;

                read = fileStream.Read(buffer, 0, headerLength);
                var dataSectionHeader = ArFileHeader.Read(buffer);
                var trimmedFileIdentifier = dataSectionHeader.FileIdentifier.TrimEnd();
                if (!trimmedFileIdentifier.EndsWith(".tar.xz"))
                {
                    throw new TorSharpException("The Debian package's data section is expected to be a .tar.xz file.");
                }

                if (fileStream.Position + dataSectionHeader.FileSize != fileStream.Length)
                {
                    throw new TorSharpException("The Debian package's data section is expected to reach the end of the .dev file.");
                }

                if (!shouldExtract)
                {
                    await ReadTarXzAsync(
                        fileStream,
                        outputDir,
                        getEntryPath,
                        shouldExtract: false).ConfigureAwait(false);
                }
                else
                {
                    await ReadTarXzAsync(
                        fileStream,
                        outputDir,
                        getEntryPath,
                        shouldExtract: true).ConfigureAwait(false);
                }
            }
        }

        private class ArFileHeader
        {
            public ArFileHeader(string fileIdentifier, uint fileSize)
            {
                FileIdentifier = fileIdentifier;
                FileSize = fileSize;
            }

            public string FileIdentifier { get; }
            public uint FileSize { get; }

            public static ArFileHeader Read(byte[] header)
            {
                var fileIdentifierString = Encoding.ASCII.GetString(header, 0, 16);

                var fileSizeString = Encoding.ASCII.GetString(header, 16 + 12 + 6 + 6 + 8, 10);
                if (!uint.TryParse(fileSizeString, out var fileSize))
                {
                    throw new TorSharpException("Could not read file size from the AR archive file header.");
                }

                return new ArFileHeader(fileIdentifierString, fileSize);
            }
        }

        public static async Task TestTarXzAsync(string tarXzPath)
        {
            await ReadTarXzAsync(
                tarXzPath,
                outputDir: null,
                getEntryPath: null,
                shouldExtract: false).ConfigureAwait(false);
        }

        public static async Task ExtractTarXzAsync(
            string tarXzPath,
            string outputDir,
            Func<string, string> getEntryPath)
        {
            await ReadTarXzAsync(
                tarXzPath,
                outputDir,
                getEntryPath,
                shouldExtract: true).ConfigureAwait(false);
        }

        private static async Task ReadTarXzAsync(
            string tarXzPath,
            string outputDir,
            Func<string, string> getEntryPath,
            bool shouldExtract)
        {
            using (var fileStream = new FileStream(tarXzPath, FileMode.Open))
            {
                await ReadTarXzAsync(
                    fileStream,
                    outputDir,
                    getEntryPath,
                    shouldExtract).ConfigureAwait(false);
            }
        }

        private static async Task ReadTarXzAsync(
            FileStream fileStream,
            string outputDir,
            Func<string, string> getEntryPath,
            bool shouldExtract)
        {
            using (var xzStream = new XZStream(fileStream))
            using (var tarReader = TarReader.Open(xzStream))
            {
                var createdDirs = new HashSet<string>();

                while (tarReader.MoveToNextEntry())
                {
                    if (!shouldExtract)
                    {
                        return;
                    }

                    if (tarReader.Entry.IsDirectory)
                    {
                        continue;
                    }

                    var entryPath = getEntryPath(tarReader.Entry.Key);
                    if (entryPath == null)
                    {
                        continue;
                    }

                    var fullEntryPath = Path.GetFullPath(Path.Combine(outputDir, entryPath));
                    var entryDir = Path.GetDirectoryName(fullEntryPath);
                    if (createdDirs.Add(entryDir))
                    {
                        Directory.CreateDirectory(entryDir);
                    }

                    using (var entryStream = tarReader.OpenEntryStream())
                    using (var outputStream = new FileStream(fullEntryPath, FileMode.Create))
                    {
                        await entryStream.CopyToAsync(outputStream).ConfigureAwait(false);
                    }
                }
            }
        }
    }
}
#endif
