using CSharpAiCli.Application;
using CSharpAiCli.Core;

namespace CSharpAiCli.Tests;

public sealed class ControlledContextApplicationServiceTests
{
    [Fact]
    public void SearchAndResolve_ReturnOpaqueRelativeDescriptors_AndRevalidateIdentity()
    {
        using TempWorkspace temp = new();
        Directory.CreateDirectory(Path.Combine(temp.Path, "src"));
        File.WriteAllText(Path.Combine(temp.Path, "src", "note.txt"), "hello");
        File.WriteAllText(Path.Combine(temp.Path, ".caicli", "secret.txt"), "secret");
        WorkspaceSnapshotProjection workspace = temp.Snapshot();
        ControlledContextApplicationService service = new();

        ApplicationResult<ControlledContextSearchProjection> search = service.Search(workspace, "note");
        ControlledContextDescriptor item = Assert.Single(search.Data!.Items);
        Assert.StartsWith("ctx_", item.SelectionId);
        Assert.Equal("src/note.txt", item.RelativePath);
        Assert.DoesNotContain(temp.Path, item.RelativePath);

        ApplicationResult<ComposerContextReferenceRecord> valid = service.Revalidate(workspace, item.SelectionId);
        Assert.True(valid.Succeeded);
        File.AppendAllText(Path.Combine(temp.Path, "src", "note.txt"), " changed");
        ApplicationResult<ComposerContextReferenceRecord> stale = service.Revalidate(workspace, item.SelectionId);
        Assert.False(stale.Succeeded);
        Assert.Equal(ApplicationErrorCategory.Conflict, stale.Error!.Category);
    }

    [Fact]
    public void Resolve_RejectsOutsidePrivateBinaryAndOversizeInputs()
    {
        using TempWorkspace temp = new();
        ControlledContextApplicationService service = new();
        WorkspaceSnapshotProjection workspace = temp.Snapshot();
        string outside = Path.Combine(Path.GetTempPath(), "outside-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(outside, "outside");
        try
        {
            Assert.Equal(ApplicationErrorCategory.Denied, service.ResolveNativePath(workspace, outside, "file").Error!.Category);
            Assert.Equal(ApplicationErrorCategory.Denied,
                service.ResolveNativePath(workspace, Path.Combine(temp.Path, ".caicli", "config.json"), "file").Error!.Category);
            string binary = Path.Combine(temp.Path, "binary.dat");
            File.WriteAllBytes(binary, [1, 0, 2]);
            Assert.Equal(ApplicationErrorCategory.Denied, service.ResolveNativePath(workspace, binary, "file").Error!.Category);
        }
        finally { File.Delete(outside); }
    }

    [Fact]
    public void External_file_is_a_task_scoped_read_only_content_hash_snapshot()
    {
        using TempWorkspace temp = new();
        string managed = System.IO.Path.Combine(temp.Path, "managed-attachments");
        ControlledContextApplicationService service = new(managed);
        WorkspaceSnapshotProjection workspace = temp.Snapshot();
        string source = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "external-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(source, "snapshot-v1");
        try
        {
            string threadId = ThreadIdentity.CreateThreadId();
            ApplicationResult<ControlledContextDescriptor> resolved = service.ResolveNativePath(workspace, source, "file", threadId);
            Assert.True(resolved.Succeeded, resolved.Error?.SafeMessage);
            ControlledContextDescriptor descriptor = Assert.IsType<ControlledContextDescriptor>(resolved.Data);
            Assert.StartsWith(DesktopContextSnapshotStore.RelativePrefix + threadId, descriptor.RelativePath);
            string snapshot = DesktopContextSnapshotStore.Resolve(workspace, descriptor.RelativePath, managed);
            Assert.True(File.GetAttributes(snapshot).HasFlag(FileAttributes.ReadOnly));
            Assert.Equal("snapshot-v1", File.ReadAllText(snapshot));
            File.WriteAllText(source, "source-changed");
            Assert.True(service.Revalidate(workspace, descriptor.SelectionId).Succeeded);
            File.SetAttributes(snapshot, FileAttributes.Normal);
            File.WriteAllText(snapshot, "tampered");
            Assert.Equal(ApplicationErrorCategory.Conflict, service.Revalidate(workspace, descriptor.SelectionId).Error!.Category);
        }
        finally { File.Delete(source); }
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "caicli-context-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            Directory.CreateDirectory(System.IO.Path.Combine(Path, ".caicli"));
            File.WriteAllText(System.IO.Path.Combine(Path, ".caicli", "config.json"), "{}");
        }
        public string Path { get; }
        public WorkspaceSnapshotProjection Snapshot()
        {
            CliEnvironmentSnapshot environment = CliEnvironmentSnapshot.Create(Path, Path, userProfile: System.IO.Path.Combine(Path, "profile"), dotnetSdkVersion: "9.0.308");
            return new WorkspaceApplicationService().Snapshot(environment).Data!;
        }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
