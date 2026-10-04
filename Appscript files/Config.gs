const CONFIG = {
  SPREADSHEET_ID: '1MLrAPAD1TP1XKVQd13BZNh6NTb98h0li4YDMq7nq6pQ',
  TIMEZONE: 'Asia/Karachi',

  SHEETS: {
    POSITIONS: 'Positions Master',
    CANDIDATES: 'Candidates',
    CRITERIA: 'JD Criteria',
    SETTINGS: 'Lists & Settings',
    TALENT_POOL: 'Talent Pool Master'
  },

  OPENAI_MODEL: 'gpt-5.6'
};


function getOpenAIKey_() {

  const key =
    PropertiesService
      .getScriptProperties()
      .getProperty(
        'OPENAI_API_KEY'
      );


  if (!key) {

    throw new Error(
      'OPENAI_API_KEY is not configured in Script Properties.'
    );

  }


  return key;
}


function getSpreadsheet_() {

  return SpreadsheetApp
    .openById(
      CONFIG.SPREADSHEET_ID
    );

}