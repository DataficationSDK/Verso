# Sharing a Notebook

A notebook in a public repository can be read as a page, with its saved outputs, by anyone with the link. There is nothing to install, nothing to sign up for, and no upload step: the page is built from the file already in your repository, so it is never out of date with what you committed.

This is the reading half of a notebook's life. The file stays the source of truth, and the page is a view of it.

## The link

A share link is the file's own link with the host changed. Take the address of the file on GitHub:

```
https://github.com/owner/repo/blob/main/analysis.verso
```

and swap the host for `www.versonotebooks.com/share/github`:

```
https://www.versonotebooks.com/share/github/owner/repo/blob/main/analysis.verso
```

Everything after the host is untouched, so you can make the change in the address bar without looking anything up. If you would rather paste than edit, [the share page](https://www.versonotebooks.com/share/) has a box that does the same swap for you.

GitLab and gists follow the same shape:

```
https://gitlab.com/group/project/-/blob/main/analysis.verso
https://www.versonotebooks.com/share/gitlab/group/project/blob/main/analysis.verso

https://gist.github.com/user/2b1f0c9e4a
https://www.versonotebooks.com/share/gist/user/2b1f0c9e4a/analysis.verso
```

GitLab projects nested several groups deep work as written. A gist needs the file name on the end, because a gist can hold more than one file.

`.verso`, `.ipynb`, and `.md` files can all be shared. Jupyter notebooks are read directly, including older ones, so you do not need to convert a notebook to share it.

## Linking to a version rather than a branch

A link to a branch shows whatever that branch holds today, which is usually what you want in a README. A link that has to keep showing the same thing, in a paper, a post, or an issue thread, should name a commit instead:

```
https://www.versonotebooks.com/share/github/owner/repo/blob/9f2c1ab.../analysis.verso
```

GitHub writes that link for you: press <kbd>y</kbd> while viewing the file and the address bar changes from the branch name to the commit it currently points at. A link pinned this way keeps showing what it showed the day you shared it, however much the branch moves afterwards.

## What the reader sees

Whatever the file already contains. A notebook saved with its outputs shows its charts, tables, images, and diagrams; one saved without them shows the code alone. That is worth knowing before you share: if you cleared outputs before committing, the page has nothing to show but source.

Nothing is executed, on the page or anywhere else. The notebook is rendered as a document, in a sandbox with no access to the site around it, which is also why a cell that expects a live kernel shows only what it last saved. Readers who want to run it can download the file and open it in Verso.

Files have to be public and under 5 MB. Private repositories are not reachable, by design: there is no place to put a token, so there is nothing to leak.

## A badge for your repository

Every shared notebook page offers the markdown for a badge, ready to paste into a README next to the notebook it points at:

```markdown
[![Open in Verso](https://www.versonotebooks.com/share/assets/open-in-verso.svg)](https://www.versonotebooks.com/share/github/owner/repo/blob/main/analysis.verso)
```

It renders as a small "Open in Verso" button, which gives anyone reading your repository a way to see the notebook without cloning it first.

## Link previews

Share links carry the notebook's title and opening paragraph as page metadata, so pasting one into Slack, Discord, or a social post produces a card describing the notebook rather than a bare URL. The title comes from the notebook's own title if it has one, and otherwise from the file name.

Notebook pages ask search engines not to index them. They reproduce files that belong to other people, and a share link is meant to be handed to someone rather than found by a stranger searching for something else.

## See also

- [Comparing Notebooks](comparing-notebooks.md), for reading what changed between two versions of a notebook
- [Markdown Notebooks](markdown-notebooks.md), for the `.md` format that renders on GitHub and runs in Verso
