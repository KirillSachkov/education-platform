using AssignmentReviewService.Core.Features.Reviews;
using AssignmentReviewService.Core.Features.Reviews.Services;
using AssignmentReviewService.Core.Vcs.Models;

namespace AssignmentReviewService.UnitTests.Reviews;

public sealed class DiffChunkerTests
{
    private static VcsDiffFile File(string path, int additions) =>
        new(path, OldPath: null, Status: "modified", additions, Deletions: 0,
            Patch: $"@@ -1 +1,{additions} @@\n", Hunks: []);

    [Fact]
    public void Prepare_RealStudentPrProfile_PassesDefaultHardCaps()
    {
        // #546 AC1: профиль etetudyyd/DirectoryService#56 (112 файлов / 6539 added строк)
        // проходит дефолтные hard-cap'ы (HardMaxFiles=150, HardMaxDiffAdditions=20000) —
        // раньше спотыкался о HardMaxFiles=60.
        AssignmentReviewLimits defaults = new();
        List<VcsDiffFile> files = [.. Enumerable.Range(0, 112).Select(i => File($"src/F{i}.cs", i == 0 ? 6539 - 111 * 58 : 58))];
        VcsDiff diff = new("sha", files, 6539, 0);

        DiffChunker.ChunkingResult result = DiffChunker.Prepare(
            diff,
            defaults.MaxDiffAdditions,
            defaults.HardMaxDiffAdditions,
            defaults.HardMaxFiles);

        Assert.False(result.ExceedsHardCap);
        Assert.Equal(112, result.FilteredFileCount);
        Assert.Equal(6539, result.FilteredAdditions);
        Assert.NotEmpty(result.Batches);
    }

    [Fact]
    public void Prepare_SmallDiff_SingleBatch()
    {
        VcsDiff diff = new("sha", [File("src/A.cs", 100), File("src/B.cs", 50)], 150, 0);

        DiffChunker.ChunkingResult result = DiffChunker.Prepare(
            diff, maxDiffAdditionsPerBatch: 1500, hardMaxAdditions: 8000, hardMaxFiles: 60);

        Assert.False(result.ExceedsHardCap);
        Assert.Single(result.Batches);
        Assert.Equal(150, result.FilteredAdditions);
        Assert.Equal(2, result.FilteredFileCount);
    }

    [Fact]
    public void Prepare_FiltersLockfilesAndGenerated()
    {
        VcsDiff diff = new(
            "sha",
            [
                File("package-lock.json", 5000),
                File("yarn.lock", 3000),
                File("node_modules/foo/index.js", 2000),
                File("dist/bundle.min.js", 1000),
                File("src/Generated.designer.cs", 800),
                File("assets/logo.png", 0),
                File("src/Real.cs", 120),
            ],
            11920,
            0);

        DiffChunker.ChunkingResult result = DiffChunker.Prepare(
            diff, maxDiffAdditionsPerBatch: 1500, hardMaxAdditions: 8000, hardMaxFiles: 60);

        Assert.False(result.ExceedsHardCap);
        Assert.Single(result.Batches);
        Assert.Equal(120, result.FilteredAdditions);
        Assert.Equal(1, result.FilteredFileCount);
        Assert.Equal("src/Real.cs", result.Batches[0].Files[0].Path);
    }

    [Fact]
    public void Prepare_LargeSourceDiff_SplitsIntoSequentialBatches()
    {
        VcsDiff diff = new(
            "sha",
            [
                File("src/A.cs", 800),
                File("src/B.cs", 800),
                File("src/C.cs", 800),
                File("src/D.cs", 800),
            ],
            3200,
            0);

        DiffChunker.ChunkingResult result = DiffChunker.Prepare(
            diff, maxDiffAdditionsPerBatch: 1500, hardMaxAdditions: 8000, hardMaxFiles: 60);

        Assert.False(result.ExceedsHardCap);
        // 800+800=1600 > 1500 → каждый batch держит ≤ 1500.
        Assert.True(result.Batches.Count >= 2);
        Assert.All(result.Batches, b => Assert.True(b.TotalAdditions <= 1500));
        // Все файлы сохранены.
        int totalFiles = result.Batches.Sum(b => b.Files.Count);
        Assert.Equal(4, totalFiles);
    }

    [Fact]
    public void Prepare_SingleFileOverLimit_GetsOwnBatch()
    {
        VcsDiff diff = new("sha", [File("src/Huge.cs", 2500), File("src/Small.cs", 100)], 2600, 0);

        DiffChunker.ChunkingResult result = DiffChunker.Prepare(
            diff, maxDiffAdditionsPerBatch: 1500, hardMaxAdditions: 8000, hardMaxFiles: 60);

        Assert.False(result.ExceedsHardCap);
        Assert.Equal(2, result.Batches.Count);
        // Гигантский файл — в собственном batch'е.
        Assert.Contains(result.Batches, b => b.Files.Count == 1 && b.Files[0].Path == "src/Huge.cs");
    }

    [Fact]
    public void Prepare_AboveHardAdditionsCap_FailsGracefully()
    {
        VcsDiff diff = new("sha", [File("src/A.cs", 9000)], 9000, 0);

        DiffChunker.ChunkingResult result = DiffChunker.Prepare(
            diff, maxDiffAdditionsPerBatch: 1500, hardMaxAdditions: 8000, hardMaxFiles: 60);

        Assert.True(result.ExceedsHardCap);
        Assert.Empty(result.Batches);
    }

    [Fact]
    public void Prepare_AboveHardFileCap_FailsGracefully()
    {
        List<VcsDiffFile> files = [.. Enumerable.Range(0, 61).Select(i => File($"src/F{i}.cs", 10))];
        VcsDiff diff = new("sha", files, 610, 0);

        DiffChunker.ChunkingResult result = DiffChunker.Prepare(
            diff, maxDiffAdditionsPerBatch: 1500, hardMaxAdditions: 8000, hardMaxFiles: 60);

        Assert.True(result.ExceedsHardCap);
    }

    [Fact]
    public void Prepare_OnlyNonReviewableFiles_NoBatches()
    {
        VcsDiff diff = new("sha", [File("package-lock.json", 5000), File("go.sum", 200)], 5200, 0);

        DiffChunker.ChunkingResult result = DiffChunker.Prepare(
            diff, maxDiffAdditionsPerBatch: 1500, hardMaxAdditions: 8000, hardMaxFiles: 60);

        Assert.False(result.ExceedsHardCap);
        Assert.Empty(result.Batches);
        Assert.Equal(0, result.FilteredAdditions);
    }

    [Fact]
    public void Prepare_FiltersPureRenamesWithNoContent()
    {
        // owlillp/DirectoryService#38: коммит-перенос 251 backend-файла (renamed, 0/0)
        // + горстка реального фронт-кода. Голые ренеймы — мусор: фильтруются, остаётся
        // только код задания. Бонус: 252 файла > HardMaxFiles=150 НЕ роняет ревью в
        // too_large, т.к. ренеймы отсеиваются ДО проверки cap'а.
        List<VcsDiffFile> files =
        [
            .. Enumerable.Range(0, 251).Select(i =>
                new VcsDiffFile($"backend/F{i}.cs", OldPath: $"F{i}.cs", Status: "renamed",
                    Additions: 0, Deletions: 0, Patch: null, Hunks: [])),
            File("frontend/src/app/page.tsx", 12),
        ];
        VcsDiff diff = new("sha", files, 12, 0);

        DiffChunker.ChunkingResult result = DiffChunker.Prepare(
            diff, maxDiffAdditionsPerBatch: 1500, hardMaxAdditions: 20000, hardMaxFiles: 150);

        Assert.False(result.ExceedsHardCap);
        Assert.Equal(1, result.FilteredFileCount);
        Assert.Single(result.Batches);
        Assert.Equal("frontend/src/app/page.tsx", result.Batches[0].Files[0].Path);
    }

    [Fact]
    public void Prepare_KeepsRenameWithContentChanges()
    {
        // Ренейм С правками кода (additions/deletions > 0) — это реальный diff, не мусор.
        VcsDiffFile renameWithEdits = new(
            "src/New.cs", OldPath: "src/Old.cs", Status: "renamed",
            Additions: 30, Deletions: 5, Patch: "@@ -1,5 +1,30 @@\n+code", Hunks: []);
        VcsDiff diff = new("sha", [renameWithEdits], 30, 5);

        DiffChunker.ChunkingResult result = DiffChunker.Prepare(
            diff, maxDiffAdditionsPerBatch: 1500, hardMaxAdditions: 20000, hardMaxFiles: 150);

        Assert.Single(result.Batches);
        Assert.Equal(1, result.FilteredFileCount);
        Assert.Equal("src/New.cs", result.Batches[0].Files[0].Path);
    }

    [Fact]
    public void Prepare_KeepsPureDeletion()
    {
        // Удаление кода (deletions > 0, additions = 0) — валидный предмет ревью, не мусор.
        VcsDiffFile deleted = new(
            "src/Gone.cs", OldPath: null, Status: "removed",
            Additions: 0, Deletions: 40, Patch: "@@ -1,40 +0,0 @@\n-code", Hunks: []);
        VcsDiff diff = new("sha", [deleted], 0, 40);

        DiffChunker.ChunkingResult result = DiffChunker.Prepare(
            diff, maxDiffAdditionsPerBatch: 1500, hardMaxAdditions: 20000, hardMaxFiles: 150);

        Assert.Single(result.Batches);
        Assert.Equal(1, result.FilteredFileCount);
    }

    [Fact]
    public void Prepare_FiltersIdeEditorAndVaultJunk_KeepsRealCode()
    {
        // #679: blackman80/template-service#10 — случайно закоммиченные IDE/редакторные
        // файлы (JetBrains `.idea/` на 95k строк, VS `.vscode/`, build-артефакты `obj/`,
        // `.DS_Store`, `*.iml`) + Obsidian-vault забивают diff и перекашивают вердикт.
        // Всё это мусор → фильтруется; остаётся только код задания.
        VcsDiff diff = new(
            "sha",
            [
                File(".idea/.idea.DirectoryService/.idea/dataSources/x.xml", 95351),
                File(".idea/workspace.xml", 473),
                File("backend/DirectoryService/.idea/vcs.xml", 7),
                File("DirectoryService.iml", 12),
                File(".vscode/launch.json", 20),
                File(".vs/config/applicationhost.config", 200),
                File("notes/.obsidian/workspace.json", 150),
                File("backend/.../obj/Debug/net10.0/App.AssemblyInfo.cs", 22),
                File(".DS_Store", 0),
                File("src/Domain/Department.cs", 140),
            ],
            96375,
            0);

        DiffChunker.ChunkingResult result = DiffChunker.Prepare(
            diff, maxDiffAdditionsPerBatch: 1500, hardMaxAdditions: 20000, hardMaxFiles: 150);

        Assert.False(result.ExceedsHardCap);
        Assert.Single(result.Batches);
        Assert.Equal(1, result.FilteredFileCount);
        Assert.Equal(140, result.FilteredAdditions);
        Assert.Equal("src/Domain/Department.cs", result.Batches[0].Files[0].Path);
    }

    [Fact]
    public void Prepare_OnlyIdeJunk_NoBatches()
    {
        // Чистый кейс blackman80#10: PR содержит ТОЛЬКО IDE-мусор, кода задания нет.
        // После фильтрации batch'ей нет → AiReviewer вернёт OFF_TOPIC (не MINOR/LOOKS_GOOD),
        // т.е. ложной авто-приёмки больше не будет.
        VcsDiff diff = new(
            "sha",
            [
                File(".idea/dataSources.local.xml", 26),
                File(".idea/dataSources/guid.xml", 95351),
            ],
            95377,
            0);

        DiffChunker.ChunkingResult result = DiffChunker.Prepare(
            diff, maxDiffAdditionsPerBatch: 1500, hardMaxAdditions: 20000, hardMaxFiles: 150);

        Assert.False(result.ExceedsHardCap);
        Assert.Empty(result.Batches);
        Assert.Equal(0, result.FilteredAdditions);
        Assert.Equal(0, result.FilteredFileCount);
    }
}
