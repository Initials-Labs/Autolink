export default {
	initialsAutoLink: {
		sectionName: 'Autolink',
		dashboardName: 'Keywords',
		heading: 'Keywords',
		refresh: 'Refresh',
		tryAgain: 'Try again',
		allLanguages: 'All languages',
		languageGroup: 'Language',

		keywordCount: (count) => (count === 1 ? '1 keyword' : `${count} keywords`),
		needingAttention: (count) => (count === 1 ? '1 needs attention' : `${count} need attention`),
		linkingOnPages: (count) => (count === 1 ? 'linking on 1 page' : `linking on ${count} pages`),
		pagesChecked: (count) => `${count} pages checked, nothing stored`,

		columnKeyword: 'Keyword',
		columnLinksTo: 'Links to',
		columnMentions: 'Mentions',

		showDetail: (keyword) => `Show detail for ${keyword}`,
		hideDetail: (keyword) => `Hide detail for ${keyword}`,
		detailFor: (keyword) => `Detail for ${keyword}`,
		external: 'external',
		unresolvedSummary: 'nothing links — the destination is gone',
		countLinked: (count) => `${count} linked`,
		countOff: (count) => `${count} off`,
		countNotLinked: (count) => `${count} not linked`,
		countNone: 'none',

		setFor: (language, who) => `Set for ${language} by ${who}.`,
		somebody: 'somebody',
		changeDestination: 'Change destination',
		removeKeyword: 'Remove keyword',
		unresolvedPageDetail:
			'The page this keyword points at is deleted, unpublished, or has no version in this language, so nothing links. Point it somewhere else, or remove the keyword.',
		unresolvedExternalDetail:
			'The stored address is not an absolute http or https URL, so nothing links. Point it somewhere else, or remove the keyword.',

		editInBackoffice: 'Open this page in the backoffice',
		viewOnSite: 'View this page on the site',

		mentionedOn: (count) => (count === 1 ? 'Mentioned on 1 page' : `Mentioned on ${count} pages`),
		noMentions: (allLanguages) =>
			`No published page in ${allLanguages ? 'any' : 'this'} language writes this word, so the link appears nowhere yet.`,
		linked: 'linked',
		doNotLinkHere: 'Do not link here',
		neverLink: 'Never link this keyword',
		switchedOff: (everywhere, allLanguages) =>
			`switched off ${everywhere ? 'everywhere' : 'here'}${allLanguages ? ', all languages' : ''}`,
		allowHere: 'Allow here',
		allowEverywhere: 'Allow everywhere',
		anotherMention: (reason) => `Another mention on this page. ${reason}`,
		notLinked: (reason) => `Not linked: ${reason}`,

		reasonSelf: 'This page is the one the keyword points at.',
		reasonHandLinked: 'Somebody has already linked to that page here.',
		reasonSkippedElement: 'The words sit inside a heading, or inside a link somebody added.',
		reasonLimit: 'Only the first mention on a page is linked.',

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

		noKeywords: (language) =>
			`No keywords in ${language} yet. Add one, and every page whose copy already writes that word links to it the next time it renders.`,
		notAuthorised: (status) =>
			`Not authorised (${status}). Your user group needs access to the Autolink section.`,
		requestFailed: (status) => `The request failed (${status}).`,

		nowLinksTo: (keyword, target) => `“${keyword}” now links to ${target}.`,
		keywordRemoved: (keyword) => `“${keyword}” has been removed.`,
		willNotLinkAnywhere: (keyword) => `“${keyword}” will not be linked anywhere.`,
		willNotLinkOn: (keyword, page) => `“${keyword}” will not be linked on ${page}.`,
		canLinkAgain: (keyword) => `“${keyword}” can link again.`,
	},
};
