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

## Writing a Message

Verso's interface is translated, so a new string usually belongs in a resource file rather than
in the code that shows it. `build/i18n/README.md` covers where each one goes and how to add it.

The question worth asking first is who reads it. A message is translated when the person reading
it can do something about it: a package that could not be downloaded, a parameter whose value does
not fit its type, a connection that has closed. Those go in a resource file with a note saying
where they appear.

Four kinds stay in English wherever they appear, and each is marked with a comment in the code
saying so:

- **Guards against programmer error.** `ArgumentNullException`, an internal
  `InvalidOperationException`, a check that a method was called before the one that sets it up.
  Nobody reading one can act on it except by changing code, and a stack trace that matches an
  issue report is worth more than a translated one that does not.
- **Protocol shape.** The host answers the editor over a small JSON-RPC surface, and a request
  missing a field it requires is a fault in the caller, not something a reader chose. Those read
  the same in every language so a log from one machine matches a search from another.
- **Anything a script reads rather than a person.** The tags a run writes on its error stream,
  and the status values in the document `--output json` produces.
- **Anything a model reads rather than a person.** The chat participant's prompt and the
  descriptions of the tools it can call.

Two shapes to avoid whatever the string says. Do not build a word from a stem and a letter
(`cell(s)`, `row(s)`, `entit{y|ies}`); write the two forms as separate entries and pick between
them with `Plural.Of`. Do not assemble a sentence from fragments; write it whole with numbered
placeholders, because another language will not put the pieces in that order.

## Pull Requests

- Target the `main` branch.
- Keep each pull request focused on a single change.
- Include tests for behavior changes. Most of Verso's behavior, including command parsing, kernel execution, formatters, and the extension model, is covered by unit and integration tests that run without an editor or notebook host.
- Make sure the solution builds and the test suite passes before requesting review.

## License

By contributing to Verso, you agree that your contributions are licensed under the [MIT License](LICENSE.md).
