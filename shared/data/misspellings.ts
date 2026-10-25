// Unambiguous everyday mistakes, caught instantly and deterministically (the AI catches them too, but not every time).
// Every key is a non-word in British, American, Canadian, Australian and Indian English; variant spellings
// (colour/color, judgement, travelling…) are deliberately absent and handled by the dialect rules instead.
// Format: wrong>right, with _ for a space in the correction.

const SPELLING = `
acheive>achieve acheived>achieved acheivement>achievement accomodate>accommodate accomodation>accommodation
accross>across adress>address agressive>aggressive alot>a_lot allready>already amature>amateur apparant>apparent
apparantly>apparently aquire>acquire arguement>argument assasination>assassination athiest>atheist basicly>basically
becuase>because beacuse>because becasue>because begining>beginning beleive>believe belive>believe bizzare>bizarre
buisness>business catagory>category cemetary>cemetery changable>changeable cheif>chief collegue>colleague
comming>coming commited>committed commitee>committee completly>completely concensus>consensus concious>conscious
curiousity>curiosity decieve>deceive definately>definitely definatly>definitely defintely>definitely
dilemna>dilemma disasterous>disastrous dissapoint>disappoint dissapointed>disappointed dissapointing>disappointing
embarass>embarrass embarassed>embarrassed embarassing>embarrassing enviroment>environment excercise>exercise
existance>existence experiance>experience explaination>explanation facinating>fascinating familar>familiar
finaly>finally foriegn>foreign fourty>forty freind>friend freinds>friends gaurd>guard goverment>government
grammer>grammar guage>gauge happend>happened harrass>harass hieght>height humourous>humorous hygene>hygiene
immediatly>immediately independant>independent intelligance>intelligence interupt>interrupt knowlege>knowledge
lenght>length liason>liaison libary>library maintainance>maintenance maintenence>maintenance medeval>medieval
millenium>millennium mischevious>mischievous mispell>misspell naturaly>naturally neccessary>necessary
necesary>necessary neccesary>necessary noticable>noticeable occassion>occasion occured>occurred
occurence>occurrence occuring>occurring ocurred>occurred paralel>parallel parliment>parliament peice>piece
percieve>perceive persistant>persistent posession>possession posible>possible potatos>potatoes preceed>precede
prefered>preferred presense>presence privelege>privilege priviledge>privilege probaly>probably
pronounciation>pronunciation publically>publicly questionaire>questionnaire realy>really recieve>receive
recieved>received reciept>receipt recomend>recommend reccomend>recommend reccommend>recommend refered>referred
relevent>relevant religous>religious remeber>remember resistence>resistance responsability>responsibility
restaraunt>restaurant rythm>rhythm sentance>sentence seperate>separate seperately>separately seperated>separated
sieze>seize similiar>similar sincerly>sincerely speach>speech strenght>strength succesful>successful
successfull>successful sucessful>successful suprise>surprise suprised>surprised tatoo>tattoo
temperture>temperature tendancy>tendency teh>the thier>their threshhold>threshold tomorow>tomorrow
tommorow>tomorrow tommorrow>tomorrow tounge>tongue truely>truly twelth>twelfth unfortunatly>unfortunately
untill>until vaccuum>vacuum vegtable>vegetable visable>visible whereever>wherever wich>which wierd>weird
wiht>with writen>written writting>writing yeild>yield
`;

// Contractions typed without the apostrophe. Only spellings that aren't words themselves: "cant", "wont", "lets",
// "ill", "id", "im" (IM), "hell", "shell", "well" and "were" are left to the AI, which can read the context.
const CONTRACTIONS = `
dont>don't doesnt>doesn't didnt>didn't isnt>isn't wasnt>wasn't werent>weren't arent>aren't havent>haven't
hasnt>hasn't hadnt>hadn't wouldnt>wouldn't couldnt>couldn't shouldnt>shouldn't mustnt>mustn't neednt>needn't
aint>ain't youre>you're theyre>they're youve>you've theyve>they've weve>we've ive>I've wouldve>would've
couldve>could've shouldve>should've mustve>must've mightve>might've youll>you'll theyll>they'll itll>it'll
thats>that's whats>what's theres>there's heres>here's wheres>where's whos>who's
`;

export type MistakeKind = 'spelling' | 'contraction';

let table: Map<string, { right: string; kind: MistakeKind }> | null = null;

function build(): Map<string, { right: string; kind: MistakeKind }> {
  const m = new Map<string, { right: string; kind: MistakeKind }>();
  const add = (src: string, kind: MistakeKind) => {
    for (const pair of src.split(/\s+/)) {
      if (!pair) continue;
      const [wrong, right] = pair.split('>');
      m.set(wrong, { right: right.replace(/_/g, ' '), kind });
    }
  };
  add(SPELLING, 'spelling');
  add(CONTRACTIONS, 'contraction');
  return m;
}

/** The correction for a known everyday mistake (case-insensitive), or undefined. */
export function knownMistake(word: string): { right: string; kind: MistakeKind } | undefined {
  table ??= build();
  return table.get(word.toLowerCase());
}

/** Every entry, for tests. */
export function allKnownMistakes(): [wrong: string, right: string, kind: MistakeKind][] {
  table ??= build();
  return [...table].map(([w, v]) => [w, v.right, v.kind]);
}
