export const SYMBOLS={
 S01:['koi','Golden Koi'],S02:['lotus','Lotus'],S03:['jade','Jade Ring'],S04:['coin','Fortune Coin'],S05:['bamboo','Bamboo'],S06:['lotus','Ruby Lotus'],
 S07:['dragon','Dragon Wild'],S08:['pearl','Free Games Pearl'],S09:['coin','Dragon Spark'],S10:['jade','Spirit Meter'],S11:['bamboo','Jade Trident'],S12:['dragon','Dragon Collector'],
 S13:['coin','Half Coin'],S14:['coin','Coin ×1'],S15:['coin','Coin ×1.5'],S16:['coin','Coin ×2'],S17:['coin','Coin ×3'],S18:['coin','Coin ×4'],S19:['coin','Coin ×5'],
 S20:['jade','Garden Seal'],S21:['pearl','Moon Seal'],S22:['dragon','Dragon Seal'],S23:['coin','Gold Seal'],
};
export const BET_POINTS=[2,4,8,20,40,80,200,400]; // 25 cents per point; UI maximum R100.
export const GAME_ID='empty-slots-v1';
export function validateGrid(grid){if(!Array.isArray(grid)||grid.length!==5||![4,6].includes(grid[0]?.length)||grid.some(reel=>!Array.isArray(reel)||reel.length!==grid[0].length||reel.some(id=>!Object.hasOwn(SYMBOLS,id))))throw Error('The server returned an unsupported Jade Dragon board. Reload to recover the settled result.');return grid;}
