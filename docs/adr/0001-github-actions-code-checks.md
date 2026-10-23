# GitHub Actions code checks

GitHub Actions runs a CI Check for every pull request and every push to `master`; the solution build and full xUnit Automated Regression Suite must pass before a pull request can merge. The Pages Demo deployment remains a separate workflow so documentation publishing keeps its existing trigger and permissions without widening code-check permissions.
