# Gentastic website

Self-contained Jekyll site for [idct.tech/gentastic](https://idct.tech/gentastic) - same approach
as the Helena and NUTS project sites: custom layouts + hand-rolled CSS only, no gem themes or
plugins, no JS framework.

## Local development

Requires Ruby 3.x + Bundler. On Windows, run these from Git Bash or WSL (the Makefile uses
POSIX shell syntax) - GNU Make itself isn't bundled with Windows; install it via Git for
Windows' optional components, `choco install make`, `scoop install make`, or WSL.

```sh
make install   # bundle install (one-time, or after editing Gemfile)
make serve     # livereload dev server at http://127.0.0.1:4000/gentastic/
make build     # production build -> ../_site (same output path CI uses)
make clean     # remove ../_site, .jekyll-cache/, .jekyll-metadata
```

Equivalent without `make`:

```sh
bundle install                                    # install
bundle exec jekyll serve --livereload              # serve
JEKYLL_ENV=production bundle exec jekyll build --destination ../_site   # build
rm -rf ../_site .jekyll-cache .jekyll-metadata      # clean
```

## Structure

- `Makefile` - `install` / `build` / `serve` / `clean` (see Local development above)
- `_config.yml` - site metadata, nav, and the repo/releases/sponsor URLs used across the site
- `_layouts/` - `default.html` (full page shell) and `page.html` (simple prose pages)
- `_includes/` - nav, footer, SEO/OG meta, inline SVG icons
- `assets/css/style.css` - the entire stylesheet (one file, organized by section)
- `assets/js/site.js` - sticky nav, mobile menu, scroll-reveal, gallery lightbox
- `assets/img/examples/` - real generations from the app (see `GENTASTIC_AUTOGEN_PROMPTS` in
  `MainWindow.xaml.cs` for how these were produced)
- `assets/img/screenshots/` - real app UI captures (`GENTASTIC_SCREENSHOT=1`)
- `assets/img/brand/` - logo/favicon/OG-image variants generated from `assets/icon-app.png`

## Contact form

`contact.html` posts to [Web3Forms](https://web3forms.com) (`api.web3forms.com/submit`) - the
same setup as the Helena and NUTS sites. The `access_key` hidden field identifies the Gentastic
form; on success Web3Forms redirects to `/contact/thank-you/` (an absolute URL, so it works from
the production domain). Spam is filtered by a hidden `botcheck` honeypot plus an hCaptcha widget
(loads `js.hcaptcha.com` on that one page only). If you rotate the key or want to disable the
captcha, edit the hidden fields / `.h-captcha` div in `contact.html`.

## Deployment

`.github/workflows/pages.yml` builds and deploys this folder to GitHub Pages on every push to
`main` that touches `website/**`. Requires the repo's **Settings → Pages → Source** set to
"GitHub Actions" (one-time, manual repo setting).

`idct.tech/gentastic` is expected to reverse-proxy to the resulting GitHub Pages project site,
the same way `idct.tech/helena` and `idct.tech/nuts` do - that routing lives outside this repo.
