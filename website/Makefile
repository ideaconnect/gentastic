.PHONY: install build serve clean

# One-time (or post-Gemfile-change) setup. Requires Ruby 3.x + Bundler.
install:
	bundle install

# Production build -> ../_site (repo root), matching the GitHub Actions deploy
# workflow (.github/workflows/pages.yml) so local output lands in the same place.
build:
	JEKYLL_ENV=production bundle exec jekyll build --destination ../_site

# Local livereload dev server at http://127.0.0.1:4000/gentastic/
serve:
	bundle exec jekyll serve --livereload

# Remove build output and Jekyll's incremental-build cache.
clean:
	rm -rf ../_site .jekyll-cache .jekyll-metadata
