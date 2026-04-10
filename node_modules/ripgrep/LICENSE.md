# License

The MIT License (MIT)

Copyright (c) 2026 Pooya Parsa <pooya@pi0.io>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.

---

## Third-party notices

This project builds and redistributes binaries of [ripgrep](https://github.com/BurntSushi/ripgrep),
Copyright (c) 2015 Andrew Gallant.

ripgrep is dual-licensed under **The MIT License** and **The Unlicense**; you
may use it under the terms of either. For ripgrep's redistribution, we rely on
the MIT license terms. The upstream license texts are preserved in this
repository at:

- [vendor/ripgrep/LICENSE-MIT](vendor/ripgrep/LICENSE-MIT)
- [vendor/ripgrep/UNLICENSE](vendor/ripgrep/UNLICENSE)
- [vendor/ripgrep/COPYING](vendor/ripgrep/COPYING)

Any distribution of the `rg` binary produced by this project must include a
copy of one of the above ripgrep license texts, per the MIT license's notice
requirement. The Unlicense imposes no such requirement, so recipients who
prefer it may instead rely on the Unlicense terms.

ripgrep transitively depends on a number of Rust crates, each under their own
permissive license (typically MIT or Apache-2.0). When shipping a compiled
`rg` binary, you should also reproduce those notices; they can be generated
from the upstream source tree with tools such as `cargo about` or
`cargo-license`.
