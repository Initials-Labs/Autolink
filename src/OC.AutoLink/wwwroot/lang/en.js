/**
 * English, and the source of truth for every string the Auto-linking screen shows.
 *
 * Registered as the base `en` culture, so it resolves for en-GB, en-US and any other English variant. Core ships its
 * own en.js the same way.
 *
 * Terms are referenced as ocAutoLink_alias. A term that needs values is a **function** taking them as arguments,
 * which is how v14 onwards does it — the older %0% token style renders literally. Functions are also why
 * pluralisation lives here rather than being assembled from fragments in the element.
 *
 * To add a language: copy this file, translate the values, and register it in umbraco-package.json with its own
 * culture.
 */
export default {
	ocAutoLink: {
		// Section and screen
		sectionName: 'Auto-linking',
		dashboardName: 'Keywords',
		heading: 'Keywords',
		refresh: 'Refresh',
		tryAgain: 'Try again',
		allLanguages: 'All languages',
		languageGroup: 'Language',

		// Summary line
		keywordCount: (count) => (count === 1 ? '1 keyword' : `${count} keywords`),
		needingDecision: 'needing a decision',
		linkingOnPages: (count) =>
			count === 1 ? 'linking on 1 page' : `linking on ${count} pages`,
		pagesChecked: (count) => `${count} pages checked, nothing stored`,

		// Columns
		columnKeyword: 'Keyword',
		columnLinksTo: 'Links to',
		columnMentions: 'Mentions',

		// Row summary
		showDetail: (keyword) => `Show detail for ${keyword}`,
		hideDetail: (keyword) => `Hide detail for ${keyword}`,
		detailFor: (keyword) => `Detail for ${keyword}`,
		contestedSummary: 'two pages claim this — choose one below',
		nothingYet: 'nothing yet',
		chosenByHand: 'chosen by hand',
		external: 'external',
		countLinked: (count) => `${count} linked`,
		countOff: (count) => `${count} off`,
		countNotLinked: (count) => `${count} not linked`,
		countNone: 'none',

		// Detail: choosing a destination
		chooseHeading: 'Choose the page this keyword should link to',
		useThis: 'Use this',
		untagInstead: 'Untagging one of them works too. This screen never edits anybody’s content.',
		externalFor: (language) =>
			`External link for ${language}. Nothing is tagged with this keyword, so removing the link removes the keyword.`,
		removeLink: 'Remove link',
		chosenFor: (language) => `Chosen by hand for ${language}.`,
		chosenNoTag: (language) => `Chosen by hand for ${language}, and no page carries this tag.`,
		undoChoice: 'Undo choice',

		// Detail: mentions
		mentionedOn: (count) =>
			count === 1 ? 'Mentioned on 1 page' : `Mentioned on ${count} pages`,
		noMentions: 'No published page in this language writes this word, so the link appears nowhere yet.',
		linked: 'linked',
		doNotLinkHere: 'Do not link here',
		neverLink: 'Never link this keyword',
		switchedOff: (everywhere, allLanguages) =>
			`switched off ${everywhere ? 'everywhere' : 'here'}${allLanguages ? ', all languages' : ''}`,
		allowHere: 'Allow here',
		allowEverywhere: 'Allow everywhere',
		anotherMention: (reason) => `Another mention on this page. ${reason}`,
		notLinked: (reason) => `Not linked: ${reason}`,

		// Why a mention was not linked. Each reads as a clause after "Not linked:" or after "Another mention on this
		// page." — so each names its own subject rather than trailing off from the sentence before it.
		reasonSelf: 'this page is the one the keyword points at.',
		reasonHandLinked: 'somebody has already linked to that page here.',
		reasonSkippedElement: 'the words sit inside a heading, or inside a link somebody added.',
		reasonLimit: 'only the first mention on a page is linked.',
		reasonContested: 'two or more pages claim this keyword, so nothing links.',

		// Adding an external link
		addExternal: 'Add external link',
		cancel: 'Cancel',
		addHeading: (culture) => `Link a keyword to somewhere outside the site, in ${culture}`,
		fieldKeyword: 'Keyword',
		fieldKeywordHint: 'Keyword, as it is written in the copy',
		fieldUrl: 'URL',
		fieldUrlHint: 'https://example.com',
		fieldTitle: 'Title',
		fieldTitleHint: 'Optional title, defaults to the host',
		nofollowLabel: 'Add rel="nofollow", so a wall of outbound links does not read as a link scheme',
		addLink: 'Add link',
		addNeedsBoth: 'A keyword and a URL are both needed.',

		// Empty and error states
		noKeywords: (culture) =>
			`No keywords in ${culture}. Tag a page in this language, or check the configured tag group matches the datatype bound to the keyword property.`,
		notAuthorised: (status) =>
			`Not authorised (${status}). Your user group needs access to the Auto-linking section.`,
		requestFailed: (status) => `The request failed (${status}).`,

		// Confirmations
		nowLinksTo: (keyword, target) => `“${keyword}” now links to ${target}.`,
		backToAutomatic: (keyword) => `“${keyword}” is back on automatic resolution.`,
		willNotLinkAnywhere: (keyword) => `“${keyword}” will not be linked anywhere.`,
		willNotLinkOn: (keyword, page) => `“${keyword}” will not be linked on ${page}.`,
		canLinkAgain: (keyword) => `“${keyword}” can link again.`,
	},
};
