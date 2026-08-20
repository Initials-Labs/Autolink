/**
 * British English, and the source of truth for every string the Auto-linking screen shows.
 *
 * Add a language by copying this file, translating the values, and registering it in umbraco-package.json with its
 * own culture. Keys are referenced as ocAutoLink_alias, and %0% style tokens are filled in order by the caller.
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
		keywordCountOne: '%0% keyword',
		keywordCountMany: '%0% keywords',
		needingDecision: 'needing a decision',
		linkingOnPagesOne: 'linking on %0% page',
		linkingOnPagesMany: 'linking on %0% pages',
		pagesChecked: '%0% pages checked, nothing stored',

		// Columns
		columnKeyword: 'Keyword',
		columnLinksTo: 'Links to',
		columnMentions: 'Mentions',

		// Row summary
		showDetail: 'Show detail for %0%',
		hideDetail: 'Hide detail for %0%',
		detailFor: 'Detail for %0%',
		contestedSummary: 'two pages claim this — choose one below',
		nothingYet: 'nothing yet',
		chosenByHand: 'chosen by hand',
		external: 'external',
		countLinked: '%0% linked',
		countOff: '%0% off',
		countNotLinked: '%0% not linked',
		countNone: 'none',

		// Detail: choosing a destination
		chooseHeading: 'Choose the page this keyword should link to',
		useThis: 'Use this',
		untagInstead: 'Untagging one of them works too. This screen never edits anybody’s content.',
		externalForCulture: 'External link for %0%. Nothing is tagged with this keyword, so removing the link removes the keyword.',
		externalForAll: 'External link for all languages. Nothing is tagged with this keyword, so removing the link removes the keyword.',
		removeLink: 'Remove link',
		chosenForCulture: 'Chosen by hand for %0%.',
		chosenForAll: 'Chosen by hand for all languages.',
		chosenNoTag: 'Chosen by hand for %0%, and no page carries this tag.',
		undoChoice: 'Undo choice',

		// Detail: mentions
		mentionedOnOne: 'Mentioned on %0% page',
		mentionedOnMany: 'Mentioned on %0% pages',
		noMentions: 'No published page in this language writes this word, so the link appears nowhere yet.',
		linked: 'linked',
		doNotLinkHere: 'Do not link here',
		neverLink: 'Never link this keyword',
		switchedOffHere: 'switched off here',
		switchedOffEverywhere: 'switched off everywhere',
		allLanguagesSuffix: ', all languages',
		allowHere: 'Allow here',
		allowEverywhere: 'Allow everywhere',
		anotherMention: 'Another mention on this page. %0%',
		notLinked: 'Not linked: %0%',

		// Why a mention was not linked. Each reads as a clause after "Not linked:" or after "Another mention on
		// this page." — so each names its own subject rather than trailing off from the sentence before it.
		reasonSelf: 'this page is the one the keyword points at.',
		reasonHandLinked: 'somebody has already linked to that page here.',
		reasonSkippedElement: 'the words sit inside a heading, or inside a link somebody added.',
		reasonLimit: 'only the first mention on a page is linked.',
		reasonContested: 'two or more pages claim this keyword, so nothing links.',

		// Adding an external link
		addExternal: 'Add external link',
		cancel: 'Cancel',
		addHeading: 'Link a keyword to somewhere outside the site, in %0%',
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
		noKeywords: 'No keywords in %0%. Tag a page in this language, or check the configured tag group matches the datatype bound to the keyword property.',
		notAuthorised: 'Not authorised (%0%). Your user group needs access to the Auto-linking section.',
		requestFailed: 'The request failed (%0%).',

		// Confirmations
		nowLinksTo: '“%0%” now links to %1%.',
		backToAutomatic: '“%0%” is back on automatic resolution.',
		willNotLinkAnywhere: '“%0%” will not be linked anywhere.',
		willNotLinkOn: '“%0%” will not be linked on %1%.',
		canLinkAgain: '“%0%” can link again.',
	},
};
