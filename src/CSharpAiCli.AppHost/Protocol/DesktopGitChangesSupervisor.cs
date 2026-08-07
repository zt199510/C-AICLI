using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using CSharpAiCli.AppHost.Protocol.Generated;

namespace CSharpAiCli.AppHost.Protocol;

/// <summary>Workspace-bound, fail-closed Git review and mutation authority for Desktop.</summary>
internal sealed class DesktopGitChangesSupervisor
{
    private const int MaxOutputChars = 262_144;
    private readonly object gate = new();
    private readonly Dictionary<string, ChangesMutateResult> mutations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ManagedWorktreeData> managedWorktrees = new(StringComparer.Ordinal);
    private readonly HashSet<string> managedBranches = new(StringComparer.Ordinal);
    private readonly string managedWorktreeRoot;
    private string? workspaceId;
    private string? workspaceRoot;
    private ChangesData? cachedSnapshot;
    private DateTimeOffset cachedAtUtc;

    public DesktopGitChangesSupervisor(string? managedWorktreeRoot = null)
    {
        this.managedWorktreeRoot = Path.GetFullPath(managedWorktreeRoot ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "C-AICLI", "worktrees"));
    }

    public void Reset(string? id = null, string? root = null)
    {
        lock (gate)
        {
            workspaceId = id;
            workspaceRoot = root is null ? null : Path.GetFullPath(root);
            cachedSnapshot = null;
            cachedAtUtc = default;
            mutations.Clear();
            managedWorktrees.Clear();
            managedBranches.Clear();
        }
    }

    public ChangesGetResult Query(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (workspaceId is null || workspaceRoot is null)
                return FailureQuery("changes-unavailable", "Changes are unavailable until a workspace is open.", "unavailable");
            try
            {
                if (cachedSnapshot is not null && DateTimeOffset.UtcNow - cachedAtUtc < TimeSpan.FromSeconds(2))
                    return SuccessQuery(cachedSnapshot);
                cachedSnapshot = CreateSnapshot(workspaceId, workspaceRoot, cancellationToken);
                cachedAtUtc = DateTimeOffset.UtcNow;
                return SuccessQuery(cachedSnapshot);
            }
            catch (GitFailure failure)
            {
                return FailureQuery(failure.Code, failure.SafeMessage, failure.Category);
            }
        }
    }

    public ChangesMutateResult Mutate(ChangesMutateParams request)
    {
        lock (gate)
        {
            if (mutations.TryGetValue(request.ClientMutationId, out ChangesMutateResult? prior)) return prior;
            ChangesMutateResult result;
            try
            {
                if (workspaceId is null || workspaceRoot is null || request.WorkspaceId != workspaceId)
                    throw new GitFailure("changes-workspace-mismatch", "The target workspace changed before the Git action.", "conflict");
                ChangesData before = CreateSnapshot(workspaceId, workspaceRoot);
                if (request.RepositoryId != before.RepositoryId || request.ExpectedRevision != before.Revision)
                    throw new GitFailure("changes-stale", "Changes changed before the mutation could be applied. Refresh and review again.", "conflict");

                string summary = Execute(request, before, workspaceRoot);
                ChangesData after = CreateSnapshot(workspaceId, workspaceRoot);
                cachedSnapshot = after;
                cachedAtUtc = DateTimeOffset.UtcNow;
                string? commitHash = request.Action == "commit" ? after.Head : null;
                result = new ChangesMutateResult
                {
                    SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
                    Succeeded = true,
                    Data = new ChangesMutationData { Action = request.Action, Summary = Bound(summary), CommitHash = commitHash, Changes = after },
                    Error = null,
                    Diagnostics = [],
                    Truncated = false
                };
            }
            catch (GitFailure failure)
            {
                result = new ChangesMutateResult
                {
                    SchemaVersion = DesktopProtocolDefinition.SchemaVersion,
                    Succeeded = false,
                    Data = null,
                    Error = Error(failure.Code, failure.Category, failure.SafeMessage, false),
                    Diagnostics = [],
                    Truncated = false
                };
            }
            if (mutations.Count >= 256) mutations.Remove(mutations.Keys.First());
            mutations[request.ClientMutationId] = result;
            return result;
        }
    }

    private string Execute(ChangesMutateParams request, ChangesData before, string root)
    {
        if (request.Action is "revert" or "commit" or "push" && !request.Confirmed)
            throw new GitFailure("changes-confirmation-required", "This Git action requires explicit confirmation.", "validation");
        if (request.Action is "commit" or "push" && request.Path is not null)
            throw new GitFailure("changes-target-invalid", "Commit and push do not accept a file target.", "validation");

        return request.Action switch
        {
            "stage" => MutateFileOrHunk(root, before, request, "staged"),
            "unstage" => MutateFileOrHunk(root, before, request, "unstaged"),
            "revert" => Revert(root, before, request),
            "commit" => Commit(root, request),
            "push" => Push(root, before, request),
            "create-branch" => CreateBranch(root, request),
            "switch-branch" => SwitchBranch(root, before, request),
            "create-worktree" => CreateWorktree(root, before, request),
            "remove-worktree" => RemoveWorktree(root, before, request),
            "remove-branch" => RemoveBranch(root, before, request),
            "create-pr" => PullRequest(root, before, request, "create"),
            "update-pr" => PullRequest(root, before, request, "edit"),
            "open-pr" => PullRequest(root, before, request, "view"),
            _ => throw new GitFailure("changes-action-invalid", "The Git action is unsupported.", "validation")
        };
    }

    private string CreateBranch(string root, ChangesMutateParams request)
    {
        string branch = RequireBranch(request.TargetBranch);
        GitResult result = Run(root, ["branch", "--", branch]);
        RequireSuccess(result, "Branch creation failed.");
        if (branch.StartsWith("caicli/", StringComparison.Ordinal)) managedBranches.Add(branch);
        return $"created branch: {branch}";
    }

    private static string SwitchBranch(string root, ChangesData before, ChangesMutateParams request)
    {
        if (before.Dirty) throw new GitFailure("changes-dirty-switch", "Commit, stage, or revert current changes before switching branches.", "conflict");
        string branch = RequireBranch(request.TargetBranch);
        GitResult result = Run(root, ["switch", "--", branch]);
        RequireSuccess(result, "Branch switch failed.");
        return $"switched branch: {branch}";
    }

    private string CreateWorktree(string root, ChangesData before, ChangesMutateParams request)
    {
        if (!request.Confirmed) throw new GitFailure("changes-confirmation-required", "Managed Worktree creation requires confirmation.", "validation");
        string threadId = request.ThreadId?.Trim() ?? "";
        if (threadId.Length == 0) throw new GitFailure("worktree-owner-required", "A task owner is required for a managed Worktree.", "validation");
        string branch = RequireBranch(request.TargetBranch);
        if (!branch.StartsWith("caicli/", StringComparison.Ordinal))
            throw new GitFailure("worktree-branch-prefix", "Managed Worktree branches must use the caicli/ prefix.", "validation");
        string worktreeId = "worktree_" + Guid.NewGuid().ToString("N");
        string managedRoot = Path.Combine(managedWorktreeRoot, before.RepositoryId!, worktreeId);
        Directory.CreateDirectory(Path.GetDirectoryName(managedRoot)!);
        bool branchExists = before.Branches?.Contains(branch, StringComparer.Ordinal) == true;
        GitResult result = branchExists
            ? Run(root, ["worktree", "add", "--", managedRoot, branch])
            : Run(root, ["worktree", "add", "-b", branch, "--", managedRoot, "HEAD"]);
        RequireSuccess(result, "Managed Worktree creation failed.");
        managedBranches.Add(branch);
        managedWorktrees[worktreeId] = new ManagedWorktreeData { WorktreeId = worktreeId, Path = managedRoot, Branch = branch, ThreadId = threadId, Dirty = false, Merged = false };
        return $"created managed Worktree {worktreeId} for {threadId}: {branch}";
    }

    private string RemoveWorktree(string root, ChangesData before, ChangesMutateParams request)
    {
        if (!request.Confirmed) throw new GitFailure("changes-confirmation-required", "Managed Worktree removal requires confirmation.", "validation");
        if (request.WorktreeId is null || !managedWorktrees.TryGetValue(request.WorktreeId, out ManagedWorktreeData? worktree))
            throw new GitFailure("worktree-not-owned", "Only Worktrees created and registered by this AppHost session can be removed.", "validation");
        bool dirty = Run(worktree.Path, ["status", "--porcelain"]).Stdout.Length != 0;
        if (dirty) throw new GitFailure("worktree-dirty", "The managed Worktree is dirty and cannot be removed.", "conflict");
        string baseBranch = ResolveBaseBranch(before, request.BaseBranch);
        bool merged = Run(root, ["merge-base", "--is-ancestor", worktree.Branch, baseBranch]).Succeeded;
        if (!merged) throw new GitFailure("worktree-unmerged", "The managed Worktree branch is not merged into the selected base.", "conflict");
        GitResult result = Run(root, ["worktree", "remove", "--", worktree.Path]);
        RequireSuccess(result, "Managed Worktree removal failed.");
        managedWorktrees.Remove(request.WorktreeId);
        return $"removed managed Worktree {request.WorktreeId}; branch {worktree.Branch} was retained";
    }

    private string RemoveBranch(string root, ChangesData before, ChangesMutateParams request)
    {
        if (!request.Confirmed) throw new GitFailure("changes-confirmation-required", "Branch removal requires separate confirmation.", "validation");
        string branch = RequireBranch(request.TargetBranch);
        if (!managedBranches.Contains(branch)) throw new GitFailure("branch-not-owned", "Only a branch registered from a managed Worktree can be removed here.", "validation");
        if (managedWorktrees.Values.Any(value => value.Branch == branch)) throw new GitFailure("branch-worktree-active", "Remove the managed Worktree before removing its branch.", "conflict");
        string baseBranch = ResolveBaseBranch(before, request.BaseBranch);
        if (!Run(root, ["merge-base", "--is-ancestor", branch, baseBranch]).Succeeded)
            throw new GitFailure("branch-unmerged", "The managed branch is not merged into the selected base.", "conflict");
        GitResult result = Run(root, ["branch", "-d", "--", branch]);
        RequireSuccess(result, "Managed branch removal failed.");
        managedBranches.Remove(branch);
        return $"removed managed branch: {branch}";
    }

    private static string PullRequest(string root, ChangesData before, ChangesMutateParams request, string operation)
    {
        if (!request.Confirmed) throw new GitFailure("changes-confirmation-required", "Pull request actions require confirmation.", "validation");
        string gh = FindExecutable("gh.exe") ?? FindExecutable("gh")
            ?? throw new GitFailure("github-cli-unavailable", "Install and sign in to GitHub CLI (gh) to use pull requests.", "unavailable");
        if (string.IsNullOrWhiteSpace(before.Branch)) throw new GitFailure("changes-detached-head", "Pull requests are unavailable from detached HEAD.", "validation");
        List<string> args;
        if (operation == "create")
        {
            string title = request.Title?.Trim() ?? "";
            if (title.Length == 0) throw new GitFailure("pull-request-title-required", "Enter a pull request title.", "validation");
            args = ["pr", "create", "--draft", "--title", title, "--body", request.Body ?? "", "--base", ResolveBaseBranch(before, request.BaseBranch), "--head", before.Branch!];
        }
        else if (operation == "edit")
        {
            string title = request.Title?.Trim() ?? "";
            if (title.Length == 0) throw new GitFailure("pull-request-title-required", "Enter a pull request title.", "validation");
            args = ["pr", "edit", "--title", title, "--body", request.Body ?? ""];
        }
        else args = ["pr", "view", "--web"];
        GitResult result = RunExecutable(gh, root, args, 60_000);
        RequireSuccess(result, "GitHub CLI pull request action failed. C-AICLI does not store a PAT.");
        return Bound(result.Output);
    }

    private static string RequireBranch(string? candidate)
    {
        string branch = candidate?.Trim() ?? "";
        bool invalid = branch.Length == 0 || branch.Length > 200 || branch.StartsWith('-') || branch.EndsWith('/') || branch.EndsWith('.') || branch.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) || branch.Contains("..", StringComparison.Ordinal) || branch.Contains("@{", StringComparison.Ordinal) || branch.Any(value => char.IsWhiteSpace(value) || value is '\\' or '~' or '^' or ':' or '?' or '*' or '[');
        if (invalid) throw new GitFailure("branch-name-invalid", "Branch name is invalid.", "validation");
        return branch;
    }

    private static string ResolveBaseBranch(ChangesData before, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return RequireBranch(requested);
        string[] preferred = before.Remote is null ? ["main", "master"] : [$"{before.Remote}/main", $"{before.Remote}/master", "main", "master"];
        return preferred.FirstOrDefault(value => before.Branches?.Contains(value, StringComparer.Ordinal) == true) ?? "main";
    }

    private static string MutateFileOrHunk(string root, ChangesData before, ChangesMutateParams request, string destination)
    {
        ChangesFileData file = ResolveFile(before, request);
        if (request.Action == "stage" && file.Area is not ("unstaged" or "untracked" or "conflicted"))
            throw new GitFailure("changes-target-invalid", "Only unstaged, untracked, or resolved conflict changes can be staged.", "validation");
        if (request.Action == "unstage" && file.Area != "staged")
            throw new GitFailure("changes-target-invalid", "Only staged changes can be unstaged.", "validation");

        GitResult result;
        if (request.HunkId is not null)
        {
            DiffHunkData hunk = file.Hunks.SingleOrDefault(value => value.HunkId == request.HunkId)
                ?? throw new GitFailure("changes-hunk-stale", "The selected hunk is no longer available.", "conflict");
            result = request.Action == "stage"
                ? Run(root, ["apply", "--cached", "--unidiff-zero", "-"], hunk.Patch)
                : Run(root, ["apply", "--cached", "--reverse", "--unidiff-zero", "-"], hunk.Patch);
        }
        else if (request.Action == "stage") result = Run(root, ["add", "--", file.Path]);
        else
        {
            result = Run(root, ["reset", "-q", "HEAD", "--", file.Path]);
            if (!result.Succeeded && before.Head == new string('0', 40)) result = Run(root, ["rm", "--cached", "--", file.Path]);
        }
        RequireSuccess(result, $"Git {request.Action} failed safely.");
        return $"{destination}: {file.Path}";
    }

    private static string Revert(string root, ChangesData before, ChangesMutateParams request)
    {
        ChangesFileData file = ResolveFile(before, request);
        if (file.Area == "untracked")
            throw new GitFailure("changes-untracked-revert-forbidden", "Untracked files are never deleted by Revert.", "validation");
        GitResult result;
        if (request.HunkId is not null)
        {
            DiffHunkData hunk = file.Hunks.SingleOrDefault(value => value.HunkId == request.HunkId)
                ?? throw new GitFailure("changes-hunk-stale", "The selected hunk is no longer available.", "conflict");
            result = Run(root, ["apply", "--reverse", "--unidiff-zero", "-"], hunk.Patch);
        }
        else
        {
            result = file.Area == "staged"
                ? Run(root, ["restore", "--source=HEAD", "--staged", "--worktree", "--", file.Path])
                : Run(root, ["restore", "--worktree", "--", file.Path]);
        }
        RequireSuccess(result, "Git revert failed safely.");
        return $"reverted: {file.Path}";
    }

    private static string Commit(string root, ChangesMutateParams request)
    {
        string message = request.Message?.Trim() ?? "";
        if (message.Length == 0) throw new GitFailure("changes-commit-message-required", "Enter a commit message.", "validation");
        GitResult staged = Run(root, ["diff", "--cached", "--quiet"]);
        if (staged.ExitCode == 0) throw new GitFailure("changes-nothing-staged", "Commit does not stage files implicitly; stage at least one change first.", "validation");
        GitResult result = Run(root, ["commit", "-m", message]);
        RequireSuccess(result, "Commit failed. Git hooks were left enabled; review their output.");
        return Bound(result.Output);
    }

    private static string Push(string root, ChangesData before, ChangesMutateParams request)
    {
        if (string.IsNullOrWhiteSpace(before.Branch)) throw new GitFailure("changes-detached-head", "Push is unavailable from detached HEAD.", "validation");
        GitResult result;
        if (string.IsNullOrWhiteSpace(before.Upstream))
        {
            if (!request.SetUpstream) throw new GitFailure("changes-upstream-required", "This branch has no upstream. Confirm Set upstream before push.", "validation");
            if (string.IsNullOrWhiteSpace(before.Remote)) throw new GitFailure("changes-remote-required", "No Git remote is configured.", "validation");
            result = Run(root, ["push", "--set-upstream", before.Remote!, before.Branch!]);
        }
        else result = Run(root, ["push"]);
        RequireSuccess(result, "Push failed without force. Review authentication, upstream, and non-fast-forward details.");
        return Bound(result.Output);
    }

    private static ChangesFileData ResolveFile(ChangesData before, ChangesMutateParams request)
    {
        if (string.IsNullOrWhiteSpace(request.Path) || string.IsNullOrWhiteSpace(request.Area))
            throw new GitFailure("changes-target-required", "Select a file or hunk first.", "validation");
        ChangesFileData? file = before.Files?.SingleOrDefault(value => value.Path == request.Path && value.Area == request.Area);
        return file ?? throw new GitFailure("changes-file-stale", "The selected file is no longer in that change area.", "conflict");
    }

    private ChangesData CreateSnapshot(string id, string root, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GitResult repository = Run(root, ["rev-parse", "--show-toplevel"], cancellationToken: cancellationToken);
        RequireSuccess(repository, "The workspace is not a supported Git repository.");
        string repoRoot = Path.GetFullPath(repository.Stdout.Trim());
        GitResult gitDirectory = Run(root, ["rev-parse", "--git-dir"], cancellationToken: cancellationToken);
        RequireSuccess(gitDirectory, "Git repository identity is unavailable.");
        string gitDirectoryValue = gitDirectory.Stdout.Trim();
        string absoluteGitDirectory = Path.GetFullPath(Path.IsPathRooted(gitDirectoryValue)
            ? gitDirectoryValue
            : Path.Combine(root, gitDirectoryValue));
        string repositoryId = Hash(repoRoot.ToUpperInvariant() + "\n" + absoluteGitDirectory.ToUpperInvariant());
        string head = Run(root, ["rev-parse", "--verify", "HEAD"], cancellationToken: cancellationToken).Stdout.Trim();
        if (head.Length != 40) head = new string('0', 40);
        string? branch = NullIfEmpty(Run(root, ["branch", "--show-current"], cancellationToken: cancellationToken).Stdout.Trim());
        string? upstream = NullIfEmpty(Run(root, ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"], cancellationToken: cancellationToken).Stdout.Trim());
        string? remote = upstream?.Split('/', 2)[0];
        string[] remotes = Run(root, ["remote"], cancellationToken: cancellationToken).Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        remote ??= remotes.FirstOrDefault(value => value == "origin") ?? remotes.FirstOrDefault();
        string[] branches = Run(root, ["for-each-ref", "--format=%(refname:short)", "refs/heads", "refs/remotes"], cancellationToken: cancellationToken)
            .Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Take(500).ToArray();
        string? compareBase = (remote is null ? ["main", "master"] : new[] { $"{remote}/main", $"{remote}/master", "main", "master" })
            .FirstOrDefault(value => branches.Contains(value, StringComparer.Ordinal) && value != branch);
        string compareDiff = compareBase is null ? "" : Bound(Run(root, ["diff", "--no-ext-diff", "--no-color", $"{compareBase}...HEAD"], cancellationToken: cancellationToken).Stdout);

        GitResult status = Run(root, ["status", "--porcelain=v1", "-z", "--untracked-files=all"], cancellationToken: cancellationToken);
        RequireSuccess(status, "Git status is unavailable.");
        GitResult unstagedDiff = Run(root, ["diff", "--no-ext-diff", "--no-color", "--unified=3"], cancellationToken: cancellationToken);
        GitResult stagedDiff = Run(root, ["diff", "--cached", "--no-ext-diff", "--no-color", "--unified=3"], cancellationToken: cancellationToken);
        RequireSuccess(unstagedDiff, "Unstaged diff is unavailable.");
        RequireSuccess(stagedDiff, "Staged diff is unavailable.");

        List<StatusEntry> entries = ParseStatus(status.Stdout);
        List<ChangesFileData> files = [];
        foreach (StatusEntry entry in entries.Take(500))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Conflicted) files.Add(CreateFile(root, entry.Path, entry.Code, "conflicted", cancellationToken));
            else if (entry.Untracked) files.Add(CreateUntracked(entry));
            else
            {
                if (entry.IndexChanged) files.Add(CreateFile(root, entry.Path, entry.Code, "staged", cancellationToken));
                if (entry.WorktreeChanged) files.Add(CreateFile(root, entry.Path, entry.Code, "unstaged", cancellationToken));
            }
        }
        string revision = Hash(repositoryId + "\n" + head + "\n" + status.Stdout + "\n" + stagedDiff.Stdout + "\n" + unstagedDiff.Stdout);
        return new ChangesData
        {
            Status = "ready",
            ExitCode = 0,
            GitStatusSummary = Bound(status.Stdout.Replace('\0', '\n')),
            GitStatusSucceeded = true,
            GitStatusErrorCode = null,
            Dirty = entries.Count != 0,
            DiffStatSummary = $"{files.Count} review projection(s): {files.Count(value => value.Area == "staged")} staged, {files.Count(value => value.Area == "unstaged")} unstaged, {files.Count(value => value.Area == "untracked")} untracked, {files.Count(value => value.Area == "conflicted")} conflicted",
            DiffSucceeded = true,
            DiffErrorCode = null,
            DiffTruncated = entries.Count > 500 || files.Any(value => value.Truncated),
            ChangedFiles = entries.Take(500).Select(value => new ChangedFileData { Path = value.Path, Status = value.Code }).ToArray(),
            SessionSource = null,
            SessionName = null,
            Warnings = entries.Count > 500 ? ["Changes list was truncated at 500 entries."] : [],
            WorkspaceId = id,
            RepositoryId = repositoryId,
            Revision = revision,
            Head = head,
            Branch = branch,
            Remote = remote,
            Upstream = upstream,
            Files = files,
            Branches = branches,
            CompareBase = compareBase,
            CompareDiff = compareDiff,
            Worktrees = managedWorktrees.Values.Select(value => value with
            {
                Dirty = Directory.Exists(value.Path) && Run(value.Path, ["status", "--porcelain"]).Stdout.Length != 0,
                Merged = compareBase is not null && Run(root, ["merge-base", "--is-ancestor", value.Branch, compareBase]).Succeeded
            }).ToArray(),
            PullRequest = null,
            GhAvailable = FindExecutable("gh.exe") is not null || FindExecutable("gh") is not null
        };
    }

    private static ChangesFileData CreateFile(string root, string path, string status, string area, CancellationToken cancellationToken = default)
    {
        List<string> args = area == "staged"
            ? ["diff", "--cached", "--no-ext-diff", "--no-color", "--unified=3", "--", path]
            : ["diff", "--no-ext-diff", "--no-color", "--unified=3", "--", path];
        GitResult result = Run(root, args, cancellationToken: cancellationToken);
        RequireSuccess(result, "A file diff is unavailable.");
        string diff = Bound(result.Stdout, out bool truncated);
        return new ChangesFileData { Path = path, Status = status, Area = area, Diff = diff, DiffIdentity = Hash(area + "\n" + diff), Hunks = ParseHunks(diff), Truncated = truncated };
    }

    private static ChangesFileData CreateUntracked(StatusEntry entry)
    {
        string diff = "Untracked file. Content is not read until explicitly staged; Revert never deletes it.";
        return new ChangesFileData { Path = entry.Path, Status = entry.Code, Area = "untracked", Diff = diff, DiffIdentity = Hash("untracked\n" + entry.Path), Hunks = [], Truncated = false };
    }

    private static IReadOnlyList<DiffHunkData> ParseHunks(string diff)
    {
        string[] lines = diff.Replace("\r\n", "\n").Split('\n');
        int first = Array.FindIndex(lines, line => line.StartsWith("@@", StringComparison.Ordinal));
        if (first < 0) return [];
        string prefix = string.Join('\n', lines.Take(first)) + "\n";
        List<DiffHunkData> hunks = [];
        for (int start = first; start < lines.Length;)
        {
            int end = start + 1;
            while (end < lines.Length && !lines[end].StartsWith("@@", StringComparison.Ordinal)) end++;
            string patch = prefix + string.Join('\n', lines[start..end]) + "\n";
            if (patch.Length <= 131_072) hunks.Add(new DiffHunkData { HunkId = Hash(patch), Header = lines[start], Patch = patch });
            start = end;
        }
        return hunks.Take(200).ToArray();
    }

    private static List<StatusEntry> ParseStatus(string value)
    {
        string[] parts = value.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        List<StatusEntry> entries = [];
        for (int index = 0; index < parts.Length; index++)
        {
            string part = parts[index];
            if (part.Length < 4) continue;
            string code = part[..2];
            string path = part[3..];
            bool rename = code[0] is 'R' or 'C' || code[1] is 'R' or 'C';
            if (rename && index + 1 < parts.Length) index++;
            bool conflict = code.Contains('U') || code is "AA" or "DD";
            entries.Add(new StatusEntry(path.Replace('\\', '/'), code, code == "??", conflict, code[0] is not (' ' or '?'), code[1] is not (' ' or '?')));
        }
        return entries;
    }

    private static GitResult Run(string root, IReadOnlyList<string> arguments, string? stdin = null, CancellationToken cancellationToken = default)
        => RunExecutable("git", root, arguments, 30_000, stdin, cancellationToken);

    private static GitResult RunExecutable(string executable, string root, IReadOnlyList<string> arguments, int timeoutMs, string? stdin = null, CancellationToken cancellationToken = default)
    {
        using Process process = new() { StartInfo = new ProcessStartInfo(executable) { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, CreateNoWindow = true } };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
        process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        try
        {
            if (!process.Start()) throw new GitFailure("git-start-failed", "Git could not be started.", "unavailable");
            if (stdin is not null) process.StandardInput.Write(stdin);
            // Never let Git inherit Desktop's framed RPC stdin. Even read-only Git
            // commands must observe EOF instead of consuming or waiting on protocol bytes.
            process.StandardInput.Close();
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeoutMs);
            try { process.WaitForExitAsync(deadline.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) {
                try { process.Kill(true); } catch (InvalidOperationException) { }
                if (cancellationToken.IsCancellationRequested) throw;
                throw new GitFailure("git-timeout", "Git exceeded its safety time bound.", "timeout");
            }
            Task.WaitAll([stdout, stderr], 2_000);
            return new GitResult(process.ExitCode, stdout.Result, stderr.Result);
        }
        catch (GitFailure) { throw; }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new GitFailure("git-unavailable", "System Git is unavailable.", "unavailable");
        }
    }

    private static string? FindExecutable(string fileName)
    {
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(folder.Trim('"'), fileName);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException) { }
        }
        return null;
    }

    private static void RequireSuccess(GitResult result, string message)
    {
        if (!result.Succeeded) throw new GitFailure("git-command-failed", message + SafeDetail(result.Stderr), "conflict");
    }

    private static string SafeDetail(string value)
    {
        string detail = Bound(value).Trim();
        return detail.Length == 0 ? "" : " " + detail;
    }

    private static string Bound(string value) => Bound(value, out _);
    private static string Bound(string value, out bool truncated)
    {
        truncated = value.Length > MaxOutputChars;
        return truncated ? value[^MaxOutputChars..] : value;
    }
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static ChangesGetResult SuccessQuery(ChangesData data) => new() { SchemaVersion = DesktopProtocolDefinition.SchemaVersion, Succeeded = true, Data = data, Error = null, Diagnostics = [], Truncated = data.DiffTruncated };
    private static ChangesGetResult FailureQuery(string code, string message, string category) => new() { SchemaVersion = DesktopProtocolDefinition.SchemaVersion, Succeeded = false, Data = null, Error = Error(code, category, message, false), Diagnostics = [], Truncated = false };
    private static ApplicationErrorData Error(string code, string category, string message, bool retryable) => new() { Code = code, Category = category, SafeMessage = message, Retryable = retryable };

    private sealed record StatusEntry(string Path, string Code, bool Untracked, bool Conflicted, bool IndexChanged, bool WorktreeChanged);
    private sealed record GitResult(int ExitCode, string Stdout, string Stderr) { public bool Succeeded => ExitCode == 0; public string Output => string.IsNullOrWhiteSpace(Stdout) ? Stderr : Stdout + (string.IsNullOrWhiteSpace(Stderr) ? "" : "\n" + Stderr); }
    private sealed class GitFailure(string code, string safeMessage, string category) : Exception(safeMessage) { public string Code { get; } = code; public string SafeMessage { get; } = safeMessage; public string Category { get; } = category; }
}
