// Dependencies
const fs = require("fs");

// Regexes
const HeaderRegex = /^\s*(?::cl:|🆑) *([a-z0-9_\-, ]+)?/img; // :cl: or 🆑 [0] followed by optional author name [1]
const EntryRegex = /^ *[*-]? *(\w+): *([^\n\r]+)\r?$/img; // * or - followed by change type [0] and change message [1]
const CommentRegex = /<!--.*?-->/gs; // HTML comments

// Main function
async function main() {
    // Check changelog directory.
    // TODO: restore this when we set up the environment variable
    /*
    if (!process.env.CHANGELOG_DIR) {
        console.log("CHANGELOG_DIR not defined, exiting.");
        return process.exit(1);
    }

    const ChangelogFilePath = `../../../${process.env.CHANGELOG_DIR}`

    if (!fs.existsSync(ChangelogFilePath)) {
        console.log(`Cannot find changelog at "${ChangelogFilePath}", exiting.`);
        return process.exit(1);
    }
    */

    const body = await getDescription();

    // Remove comments from the body
    commentlessBody = body.replace(CommentRegex, '');

    // Get author
    const headerMatch = HeaderRegex.exec(commentlessBody);
    if (!headerMatch) {
        console.log("No changelog entry found.");
        return;
    }

    let author = headerMatch[1];
    if (author) {
        author = author.trim();
    }

    // Offset results past the header
    commentlessBody = commentlessBody.slice(HeaderRegex.lastIndex);

    // Get all changes from the body
    const results = getChanges(commentlessBody);

    if (results.entries.length <= 0)
    {
        console.log("PR has a changelog header but no valid entries. Either remove the changelog completely, or use entries of the format '- add: text', '- remove: text', '- tweak: text', or '- fix: text'.");
        return process.exit(1);
    }

    let success = true;
    results.errors.forEach((entry) => {
        console.log(`Invalid changelog entry: "${entry.type}" with message "${entry.message}"`);
        success = false;
    });

    if (!success)
        return process.exit(1);

    console.log("Changelog is valid.")
    if (author)
        console.log(`Author: "${author}"`)
    console.log("Entries:");
    results.entries.forEach((entry) => {
        console.log(`${entry.type}: ${entry.message}`);
    });
}

// Get the PR/MR description from GitLab CI, falling back to a GitHub event payload
async function getDescription() {
    if (process.env.CI_MERGE_REQUEST_IID) {
        let description = process.env.CI_MERGE_REQUEST_DESCRIPTION || '';

        // GitLab truncates the predefined variable, so fetch the full text from the API.
        if (process.env.CI_MERGE_REQUEST_DESCRIPTION_IS_TRUNCATED === 'true') {
            const url = `${process.env.CI_API_V4_URL}/projects/${process.env.CI_MERGE_REQUEST_PROJECT_ID}/merge_requests/${process.env.CI_MERGE_REQUEST_IID}`;
            const headers = process.env.CHANGELOG_GITLAB_TOKEN
                ? { 'PRIVATE-TOKEN': process.env.CHANGELOG_GITLAB_TOKEN }
                : { 'JOB-TOKEN': process.env.CI_JOB_TOKEN };
            const response = await fetch(url, { headers });
            if (!response.ok) {
                console.error(`Failed to fetch full merge request description: HTTP ${response.status}`);
                process.exit(1);
            }
            description = (await response.json()).description || '';
        }

        return description;
    }

    const eventPath = process.env.GITHUB_EVENT_PATH;
    if (!eventPath) {
        console.error("Neither CI_MERGE_REQUEST_IID nor GITHUB_EVENT_PATH is set.");
        process.exit(1);
    }
    const event = JSON.parse(fs.readFileSync(eventPath, 'utf8'));
    return event.pull_request && event.pull_request.body ? event.pull_request.body : '';
}

// Get all changes from the PR body
function getChanges(body) {
    const matches = [];
    const entries = [];
    const errors = [];

    for (const match of body.matchAll(EntryRegex)) {
        matches.push([match[1], match[2]]);
    }

    if (!matches)
    {
        console.log("No changelog entries found.");
        return;
    }

    // Check change types and construct changelog entry
    matches.forEach((entry) => {
        let type;

        switch (entry[0].toLowerCase()) {
            case "add":
                type = "Add";
                break;
            case "remove":
                type = "Remove";
                break;
            case "tweak":
                type = "Tweak";
                break;
            case "fix":
                type = "Fix";
                break;
            default:
                break;
        }

        if (type) {
            entries.push({
                type: type,
                message: entry[1],
            });
        } else {
            errors.push({
                type: entry[0],
                message: entry[1]
            });
        }
    });

    return {entries: entries, errors: errors};
}

// Run main
main();
