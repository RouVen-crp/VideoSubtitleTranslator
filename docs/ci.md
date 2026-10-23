# Continuous integration

The code CI workflow runs automatically for pull requests targeting any branch and for pushes to `master`. It restores and builds `VideoSubtitleTranslator.sln`, then runs the complete `VideoSubtitleTranslator.Tests` xUnit suite with the .NET 10 SDK on GitHub-hosted Ubuntu, macOS, and Windows runners. NuGet packages are cached between runs based on the project files. No API key, video, Python environment, yt-dlp, or ffmpeg is needed by this suite.

Every matrix job must pass for the workflow check to pass. The workflow reports the OS in each job name and allows the other OS jobs to finish when one fails. Configure the repository's `master` branch ruleset or branch protection to require the workflow's job check before merging; workflow YAML cannot enable that repository-level setting by itself. Keep the Pages Demo deployment workflow separate: it continues to deploy on `master` pushes that change `docs/**`.

The CI workflow should use read-only `contents` permission, avoid secrets, and disable overlapping runs for the same pull request or branch so stale runs do not consume runner time. `pull_request` events also let GitHub run untrusted fork contributions without exposing repository secrets.
