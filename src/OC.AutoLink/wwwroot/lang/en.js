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
		needingAttention: (count) => (count === 1 ? '1 needs attention' : `${count} need attention`),
		linkingOnPages: (count) => (count === 1 ? 'linking on 1 page' : `linking on ${count} pages`),
		pagesChecked: (count) => `${count} pages checked, nothing stored`,

		// Columns
		columnKeyword: 'Keyword',
		columnLinksTo: 'Links to',
		columnMentions: 'Mentions',

		// Row summary
		showDetail: (keyword) => `Show detail for ${keyword}`,
		hideDetail: (keyword) => `Hide detail for ${keyword}`,
		detailFor: (keyword) => `Detail for ${keyword}`,
		external: 'external',
		unresolvedSummary: 'nothing links — the destination is gone',
		countLinked: (count) => `${count} linked`,
		countOff: (count) => `${count} off`,
		countNotLinked: (count) => `${count} not linked`,
		countNone: 'none',

		// Detail: the destination
		setFor: (language, who) => `Set for ${language} by ${who}.`,
		somebody: 'somebody',
		changeDestination: 'Change destination',
		removeKeyword: 'Remove keyword',
		unresolvedPageDetail:
			'The page this keyword points at is deleted, unpublished, or has no version in this language, so nothing links. Point it somewhere else, or remove the keyword.',
		unresolvedExternalDetail:
			'The stored address is not an absolute http or https URL, so nothing links. Point it somewhere else, or remove the keyword.',

		// Detail: mentions
		mentionedOn: (count) => (count === 1 ? 'Mentioned on 1 page' : `Mentioned on ${count} pages`),
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

		// Adding a keyword and changing where one points
		addKeyword: 'Add keyword',
		cancel: 'Cancel',
		addHeading: (language) => `Add a keyword for ${language}`,
		editHeading: (keyword, language) => `Where “${keyword}” links to, in ${language}`,
		fieldKeyword: 'Keyword',
		fieldKeywordHint: 'Keyword, as it is written in the copy',
		fieldDestination: 'Links to',
		fieldTitle: 'Title',
		fieldTitleHint: 'Optional title, defaults to the host',
		nofollowLabel: 'Add rel="nofollow", so a wall of outbound links does not read as a link scheme',
		saveKeyword: 'Save keyword',
		thePage: 'the page',
		needsKeywordAndDestination: 'A keyword and a destination are both needed.',
		notAbsoluteUrl: 'An address outside the site has to start with http:// or https://.',
		mediaNotSupported: 'A keyword can link to a page or to an address outside the site. Media is not supported.',
		targetNotUsed: 'Auto-links never open a new window, so that choice will not be used.',

		// Empty and error states
		noKeywords: (language) =>
			`No keywords in ${language} yet. Add one, and every page whose copy already writes that word links to it the next time it renders.`,
		notAuthorised: (status) =>
			`Not authorised (${status}). Your user group needs access to the Auto-linking section.`,
		requestFailed: (status) => `The request failed (${status}).`,

		// Confirmations
		nowLinksTo: (keyword, target) => `“${keyword}” now links to ${target}.`,
		keywordRemoved: (keyword) => `“${keyword}” has been removed.`,
		willNotLinkAnywhere: (keyword) => `“${keyword}” will not be linked anywhere.`,
		willNotLinkOn: (keyword, page) => `“${keyword}” will not be linked on ${page}.`,
		canLinkAgain: (keyword) => `“${keyword}” can link again.`,
	},
};
