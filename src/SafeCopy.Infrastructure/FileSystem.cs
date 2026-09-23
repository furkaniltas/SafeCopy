namespace SafeCopy.Infrastructure;

using SafeCopy.Core.Abstractions;
using SafeCopy.Core.Models;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using CoreHashAlgorithm = SafeCopy.Core.Abstractions.HashAlgorithm;
using CoreSearchOption = SafeCopy.Core.Abstractions.SearchOption;

public sealed class FileSystem : IFileSystem
{
    public Result<bool> Exists(string path)
    {
        try { return Result<bool>.Success(File.Exists(path) || Directory.Exists(path)); }
        catch (Exception ex) { return Result<bool>.Failure(Error.IoError(ex.Message, ex)); }
    }
    
    public Result<Stream> OpenRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return Result<Stream>.Failure(Error.NotFound($"File not found: {path}"));
            return Result<Stream>.Success(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        }
        catch (Exception ex) { return Result<Stream>.Failure(Error.IoError(ex.Message, ex)); }
    }
    
    public Result<Stream> OpenWrite(string path, bool overwrite = false)
    {
        try
        {
            var mode = overwrite ? FileMode.Create : FileMode.CreateNew;
            return Result<Stream>.Success(File.Open(path, mode, FileAccess.Write, FileShare.None));
        }
        catch (Exception ex) { return Result<Stream>.Failure(Error.IoError(ex.Message, ex)); }
    }
    
    public Result<long> GetSize(string path)
    {
        try
        {
            if (Directory.Exists(path) && path.EndsWith(".udf", StringComparison.OrdinalIgnoreCase))
            {
                var contentPath = Path.Combine(path, "content.xml");
                if (!File.Exists(contentPath))
                    contentPath = Path.Combine(path, "content");
                if (File.Exists(contentPath))
                    return Result<long>.Success(new FileInfo(contentPath).Length);
                // Fallback: sum directory size
                var total = Directory.GetFiles(path, "*", System.IO.SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
                return Result<long>.Success(total);
            }
            if (!File.Exists(path)) return Result<long>.Failure(Error.NotFound($"File not found: {path}"));
            return Result<long>.Success(new FileInfo(path).Length);
        }
        catch (Exception ex) { return Result<long>.Failure(Error.IoError(ex.Message, ex)); }
    }
    
    public Result<DateTime> GetLastWriteTimeUtc(string path)
    {
        try
        {
            if (!File.Exists(path)) return Result<DateTime>.Failure(Error.NotFound($"File not found: {path}"));
            return Result<DateTime>.Success(File.GetLastWriteTimeUtc(path));
        }
        catch (Exception ex) { return Result<DateTime>.Failure(Error.IoError(ex.Message, ex)); }
    }
    
    public Result<string> ComputeHash(string path, CoreHashAlgorithm algorithm = CoreHashAlgorithm.SHA256)
    {
        try
        {
            if (Directory.Exists(path) && path.EndsWith(".udf", StringComparison.OrdinalIgnoreCase))
            {
                var contentPath = Path.Combine(path, "content.xml");
                if (!File.Exists(contentPath))
                    contentPath = Path.Combine(path, "content");
                if (File.Exists(contentPath))
                    path = contentPath;
                else if (Directory.Exists(path))
                {
                    // Hash directory listing as fallback
                    using var hasher2 = algorithm switch
                    {
                        CoreHashAlgorithm.SHA256 => (System.Security.Cryptography.HashAlgorithm)System.Security.Cryptography.SHA256.Create(),
                        CoreHashAlgorithm.SHA512 => (System.Security.Cryptography.HashAlgorithm)System.Security.Cryptography.SHA512.Create(),
                        CoreHashAlgorithm.MD5 => (System.Security.Cryptography.HashAlgorithm)System.Security.Cryptography.MD5.Create(),
                        _ => (System.Security.Cryptography.HashAlgorithm)System.Security.Cryptography.SHA256.Create()
                    };
                    var files = Directory.GetFiles(path, "*", System.IO.SearchOption.AllDirectories).OrderBy(f => f);
                    foreach (var f in files)
                    {
                        var bytes = System.Text.Encoding.UTF8.GetBytes(f);
                        hasher2.TransformBlock(bytes, 0, bytes.Length, null, 0);
                    }
                    hasher2.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    var hash2 = hasher2.Hash!;
                    return Result<string>.Success(Convert.ToHexString(hash2));
                }
            }
            if (!File.Exists(path)) return Result<string>.Failure(Error.NotFound($"File not found: {path}"));
            
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var hasher = algorithm switch
            {
                CoreHashAlgorithm.SHA256 => (System.Security.Cryptography.HashAlgorithm)System.Security.Cryptography.SHA256.Create(),
                CoreHashAlgorithm.SHA512 => (System.Security.Cryptography.HashAlgorithm)System.Security.Cryptography.SHA512.Create(),
                CoreHashAlgorithm.MD5 => (System.Security.Cryptography.HashAlgorithm)System.Security.Cryptography.MD5.Create(),
                _ => (System.Security.Cryptography.HashAlgorithm)System.Security.Cryptography.SHA256.Create()
            };
            
            var hash = hasher.ComputeHash(stream);
            return Result<string>.Success(Convert.ToHexString(hash));
        }
        catch (Exception ex) { return Result<string>.Failure(Error.IoError(ex.Message, ex)); }
    }
    
    public Result<IReadOnlyList<string>> EnumerateFiles(string directory, string pattern = "*", CoreSearchOption option = CoreSearchOption.TopDirectoryOnly)
    {
        try
        {
            if (!Directory.Exists(directory)) return Result<IReadOnlyList<string>>.Failure(Error.NotFound($"Directory not found: {directory}"));
            
            var searchOption = option == CoreSearchOption.AllDirectories ? System.IO.SearchOption.AllDirectories : System.IO.SearchOption.TopDirectoryOnly;
            var files = Directory.GetFiles(directory, pattern, searchOption).ToList();
            return Result<IReadOnlyList<string>>.Success(files.AsReadOnly());
        }
        catch (Exception ex) { return Result<IReadOnlyList<string>>.Failure(Error.IoError(ex.Message, ex)); }
    }
    
    public Result CreateDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return Result.Success();
        }
        catch (Exception ex) { return Result.Failure(Error.IoError(ex.Message, ex)); }
    }
    
    public Result DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
                File.Delete(path);
            }
            return Result.Success();
        }
        catch (Exception ex) { return Result.Failure(Error.IoError(ex.Message, ex)); }
    }
    
    public Result DeleteDirectory(string path, bool recursive = false)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive);
            return Result.Success();
        }
        catch (Exception ex) { return Result.Failure(Error.IoError(ex.Message, ex)); }
    }
    
    public Result CopyFile(string source, string destination, bool overwrite = false)
    {
        try
        {
            File.Copy(source, destination, overwrite);
            return Result.Success();
        }
        catch (Exception ex) { return Result.Failure(Error.IoError(ex.Message, ex)); }
    }
    
    public Result MoveFile(string source, string destination)
    {
        try
        {
            File.Move(source, destination);
            return Result.Success();
        }
        catch (Exception ex) { return Result.Failure(Error.IoError(ex.Message, ex)); }
    }
    
    public Result<bool> IsReadOnly(string path)
    {
        try
        {
            if (!File.Exists(path)) return Result<bool>.Failure(Error.NotFound($"File not found: {path}"));
            var attrs = File.GetAttributes(path);
            return Result<bool>.Success((attrs & FileAttributes.ReadOnly) != 0);
        }
        catch (Exception ex) { return Result<bool>.Failure(Error.IoError(ex.Message, ex)); }
    }
    
    public Result SetReadOnly(string path, bool readOnly)
    {
        try
        {
            if (!File.Exists(path)) return Result.Failure(Error.NotFound($"File not found: {path}"));
            var attrs = File.GetAttributes(path);
            attrs = readOnly ? attrs | FileAttributes.ReadOnly : attrs & ~FileAttributes.ReadOnly;
            File.SetAttributes(path, attrs);
            return Result.Success();
        }
        catch (Exception ex) { return Result.Failure(Error.IoError(ex.Message, ex)); }
    }
    
    public Result<string> GetTempFileName(string? extension = null)
    {
        try
        {
            var name = Path.GetRandomFileName();
            if (extension != null) name = Path.ChangeExtension(name, extension);
            var path = Path.Combine(Path.GetTempPath(), name);
            return Result<string>.Success(path);
        }
        catch (Exception ex) { return Result<string>.Failure(Error.IoError(ex.Message, ex)); }
    }
    
    public Result<string> GetTempDirectory()
    {
        try { return Result<string>.Success(Path.GetTempPath()); }
        catch (Exception ex) { return Result<string>.Failure(Error.IoError(ex.Message, ex)); }
    }
}

public sealed class SecureTempWorkspace : ITempWorkspace
{
    private readonly IFileSystem _fileSystem;
    private readonly string _sessionId;
    private readonly string _rootPath;
    private bool _disposed;
    
    public string RootPath => _rootPath;
    public string InputPath => Path.Combine(_rootPath, "input");
    public string ExtractedPath => Path.Combine(_rootPath, "extracted");
    public string OcrPath => Path.Combine(_rootPath, "ocr");
    public string OutputPath => Path.Combine(_rootPath, "output");
    public string VerificationPath => Path.Combine(_rootPath, "verification");
    
    public SecureTempWorkspace(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
        _sessionId = Guid.NewGuid().ToString("N");
        _rootPath = Path.Combine(Path.GetTempPath(), "SafeCopy", _sessionId);

        // Create root first with secure ACL, then sub-directories.
        _fileSystem.CreateDirectory(_rootPath);
        SetSecureAcl(_rootPath);

        foreach (var dir in new[] { InputPath, ExtractedPath, OcrPath, OutputPath, VerificationPath })
        {
            _fileSystem.CreateDirectory(dir);
            SetSecureAcl(dir);
        }
    }

    ~SecureTempWorkspace()
    {
        try { Cleanup(); } catch { /* finalizer must not throw */ }
    }
    
    private void SetSecureAcl(string path)
    {
        try
        {
            var dirInfo = new DirectoryInfo(path);
            var security = dirInfo.GetAccessControl();
            security.SetAccessRuleProtection(true, false);
            
            var user = WindowsIdentity.GetCurrent().User;
            if (user == null) return; // Best effort - cannot set ACL without user
            
            var rule = new FileSystemAccessRule(
                user,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow);
            
            security.SetAccessRule(rule);
            dirInfo.SetAccessControl(security);
        }
        catch { /* ACL setting best effort */ }
    }
    
    public Result<string> CreateInputFile(string originalName)
    {
        var safeName = SanitizeFileName(originalName);
        var path = Path.Combine(InputPath, safeName);
        return Result<string>.Success(path);
    }
    
    public Result<string> CreateExtractedFile(string name)
    {
        var safeName = SanitizeFileName(name);
        var path = Path.Combine(ExtractedPath, safeName);
        return Result<string>.Success(path);
    }
    
    public Result<string> CreateOcrFile(string name)
    {
        var safeName = SanitizeFileName(name);
        var path = Path.Combine(OcrPath, safeName);
        return Result<string>.Success(path);
    }
    
    public Result<string> CreateOutputFile(string name)
    {
        var safeName = SanitizeFileName(name);
        var path = Path.Combine(OutputPath, safeName);
        return Result<string>.Success(path);
    }
    
    public Result<string> CreateVerificationFile(string name)
    {
        var safeName = SanitizeFileName(name);
        var path = Path.Combine(VerificationPath, safeName);
        return Result<string>.Success(path);
    }
    
    public Result<string> GetUniqueFileName(string directory, string prefix, string extension)
    {
        var fullDir = directory switch
        {
            "input" => InputPath,
            "extracted" => ExtractedPath,
            "ocr" => OcrPath,
            "output" => OutputPath,
            "verification" => VerificationPath,
            _ => Path.Combine(_rootPath, directory)
        };
        
        _fileSystem.CreateDirectory(fullDir);
        
        var name = $"{prefix}_{Guid.NewGuid():N}.{extension.TrimStart('.')}";
        return Result<string>.Success(Path.Combine(fullDir, name));
    }
    
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
    
    public Result Cleanup()
    {
        if (_disposed) return Result.Success();
        
        try
        {
            if (Directory.Exists(_rootPath))
            {
                foreach (var file in Directory.GetFiles(_rootPath, "*", System.IO.SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); } catch { }
                }
                
                foreach (var dir in Directory.GetDirectories(_rootPath, "*", System.IO.SearchOption.AllDirectories)
                                          .OrderByDescending(d => d.Length))
                {
                    try { Directory.Delete(dir, true); } catch { }
                }
                
                try { Directory.Delete(_rootPath, true); } catch { }
            }
            
            _disposed = true;
            return Result.Success();
        }
        catch (Exception ex) { return Result.Failure(Error.IoError(ex.Message, ex)); }
    }
    
    public async Task<Result> CleanupAsync()
    {
        return await Task.Run(Cleanup).ConfigureAwait(false);
    }
    
    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await CleanupAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
    
    public static void CleanupStaleWorkspaces(TimeSpan maxAge)
    {
        var root = Path.Combine(Path.GetTempPath(), "SafeCopy");
        if (!Directory.Exists(root)) return;
        
        var cutoff = DateTime.UtcNow - maxAge;
        
        foreach (var dir in Directory.GetDirectories(root))
        {
            try
            {
                var creationTime = Directory.GetCreationTimeUtc(dir);
                if (creationTime < cutoff)
                {
                    DeleteDirectoryRecursive(dir);
                }
            }
            catch { /* Ignore - may be in use by another session */ }
        }
    }
    
    private static void DeleteDirectoryRecursive(string path)
    {
        try
        {
            foreach (var file in Directory.GetFiles(path, "*", System.IO.SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); } catch { }
            }
            
            foreach (var dir in Directory.GetDirectories(path, "*", System.IO.SearchOption.AllDirectories)
                                          .OrderByDescending(d => d.Length))
            {
                try { Directory.Delete(dir, true); } catch { }
            }
            
            Directory.Delete(path, true);
        }
        catch { /* Best effort */ }
    }
}