# SDK documentation on GitHub Pages

The SDK repository is [auroracorelabs/artifactd-sdk-client](https://github.com/auroracorelabs/artifactd-sdk-client). It owns its SDK guides and generated language API references. Server documentation remains at [docs.artifactd.net](https://docs.artifactd.net/).

## Publication

The [SDK documentation workflow](https://github.com/auroracorelabs/artifactd-sdk-client/actions/workflows/docs.yml) builds the site on pull requests, pushes to main and manual runs. Pull requests build and validate the Pages artifact. Deployment runs only for the repository's default branch, using the github-pages environment.

In the GitHub repository, open **Settings → Pages → Build and deployment → Source** and select **GitHub Actions**. Then push the prepared documentation changes to main, or run the workflow from that branch. The deployment job reports the actual published URL through its environment output.

For this repository, the expected project-site URL after the first successful deployment is:

https://auroracorelabs.github.io/artifactd-sdk-client/

This URL is not a claim that the site has already been activated. It becomes available after Pages configuration and a successful deployment. No custom domain or gh-pages source branch is required. The workflow uses the built-in GITHUB_TOKEN and Pages OIDC permissions; no additional deployment token is needed.

## Content and API generation

The site separates shared setup, language-specific integration and runnable examples:
- Getting started covers downloads, shared configuration and first-report verification. Its environment-variable table comes from the repository README.
- Language guides reuse package READMEs, link to runnable examples and retain language-specific APIs and advanced configuration. Package READMEs remain self-contained for GitHub and package consumers.
- Runnable examples include the actual source files under examples and the .NET sample, plus execution commands.
- Queues and delivery explains shared lifecycle behavior. The protocol page records the wire contract.
- The .NET compatibility and integration READMEs render as one guide; the previous integration URL redirects to it.

Edit those source files to update their content. Site-specific guides and assets live in docs/site.

API references are generated directly from the SDK source:

| Language | Generator | Source |
| --- | --- | --- |
| .NET | DocFX | The .NET Standard 2.0 library project |
| Python | pdoc | The public artifactd_client_feedback module |
| Node.js / TypeScript | TypeDoc | The package's TypeScript declarations |
| Rust | rustdoc | The public crate |
| PHP | PHP reflection | Public types, properties, docblocks and method signatures |

Versions of Node dependencies are locked in docs/site/package-lock.json. Python documentation dependencies are pinned in docs/site/requirements.txt and DocFX is pinned in the local tool manifest. Rust dependencies use the crate lockfile.

## Debug in VS Code

Open the SDK repository, select **SDK documentation [8090]** under Run and Debug, and press F5. The pre-launch task installs documentation dependencies in a local Python virtual environment, generates all five API references and builds the site. VS Code then starts the preview and opens http://127.0.0.1:8090/artifactd-sdk-client/ in your browser. Stop debugging to stop the server.

The same launch configuration is available from the sibling Artifactd server checkout. Its **All documentation** compound starts both the developer documentation on port 8091 and SDK documentation on port 8090.

After a build, you can also run the preview with `npm run dev --prefix docs/site` (runs `node docs/site/serve.ts`). Source edits require another build or restarting the debug configuration.

## Local build

Install Node.js 24+ (docs/site TypeScript via native stripping), Python 3.10+, .NET SDK 10, stable Rust and PHP 8.2+ with cURL and JSON (or Docker for generating the PHP API reference). From the repository root:

    python3 -m venv .venv
    # Activate the virtual environment for your shell.
    python -m pip install -r docs/site/requirements.txt
    npm ci --prefix docs/site
    bash docs/site/build-api.sh
    npm run build --prefix docs/site
    npm test --prefix docs/site

The complete static site is written to dist/docs. Generated API intermediates are stored under build; both directories are ignored. Builds fail if a generated language reference is missing.

The default base path is /artifactd-sdk-client/. To preview under that project path, create a temporary web root containing a directory named artifactd-sdk-client with the contents of dist/docs, then serve that web root. Alternatively, build for a root-mounted local server:

    DOCS_BASE_PATH=/ npm run build --prefix docs/site
    DOCS_BASE_PATH=/ npm test --prefix docs/site
    python3 -m http.server 8090 --directory dist/docs

The local preview is then http://localhost:8090/. The workflow sets the production origin and repository base path explicitly.

## Checks

The documentation checks validate the generated language entry points, guide links, anchors, search URLs and Pages base path. API references use their native generators' search and navigation. The guide search covers authored guides and runnable examples. The generated llms.txt links both guides and language references for AI discovery.
