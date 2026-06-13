# Contributing to Verso

Thank you for your interest in contributing to Verso. This document covers the mechanics of getting a change accepted.

## Before You Start

Open an issue describing what you'd like to work on before sending a substantial pull request. This avoids duplicated effort and lets us confirm the change fits the framework's direction. Small fixes (typos, doc corrections, obvious bugs) can go straight to a pull request.

## Developer Certificate of Origin

Verso accepts contributions under the [Developer Certificate of Origin, version 1.1](https://developercertificate.org/) (DCO). The DCO is a lightweight attestation that you wrote the contribution or otherwise have the right to submit it under the project's MIT license. There is no contributor license agreement to sign.

Every commit must carry a `Signed-off-by` line with your real name and email address:

```
Signed-off-by: Jane Developer <jane@example.com>
```

Git adds this for you when you commit with the `-s` flag:

```bash
git commit -s -m "Fix cell output formatting for nested data frames"
```

If you forget the sign-off on a commit, you can amend it:

```bash
git commit --amend -s --no-edit
```

or sign off an entire branch:

```bash
git rebase --signoff main
```

By signing off, you certify the following:

```
Developer Certificate of Origin
Version 1.1

Copyright (C) 2004, 2006 The Linux Foundation and its contributors.

Everyone is permitted to copy and distribute verbatim copies of this
license document, but changing it is not allowed.


Developer's Certificate of Origin 1.1

By making a contribution to this project, I certify that:

(a) The contribution was created in whole or in part by me and I
    have the right to submit it under the open source license
    indicated in the file; or

(b) The contribution is based upon previous work that, to the best
    of my knowledge, is covered under an appropriate open source
    license and I have the right under that license to submit that
    work with modifications, whether created in whole or in part
    by me, under the same open source license (unless I am
    permitted to submit under a different license), as indicated
    in the file; or

(c) The contribution was provided directly to me by some other
    person who certified (a), (b) or (c) and I have not modified
    it.

(d) I understand and agree that this project and the contribution
    are public and that a record of the contribution (including all
    personal information I submit with it, including my sign-off) is
    maintained indefinitely and may be redistributed consistent with
    this project or the open source license(s) involved.
```

## Pull Requests

- Target the `main` branch.
- Keep each pull request focused on a single change.
- Include tests for behavior changes. Most of Verso's behavior, including command parsing, kernel execution, formatters, and the extension model, is covered by unit and integration tests that run without an editor or notebook host.
- Make sure the solution builds and the test suite passes before requesting review.

## License

By contributing to Verso, you agree that your contributions are licensed under the [MIT License](LICENSE.md).
