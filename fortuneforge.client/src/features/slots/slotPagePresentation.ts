export const creditFormatter = new Intl.NumberFormat('en-US')

export const formatRand = (amount: number) => `R${creditFormatter.format(amount)}`
