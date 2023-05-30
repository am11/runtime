// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include <minipal/utf8.h>

#include <errno.h>
#include <limits.h>
#include <string.h>
#include <assert.h>

#define FASTLOOP

#ifdef TARGET_WINDOWS
#define W(str) L ## str
#else
#define W(str) u##str
#endif

#define HIGH_SURROGATE_START 0xd800
#define HIGH_SURROGATE_END 0xdbff
#define LOW_SURROGATE_START 0xdc00
#define LOW_SURROGATE_END 0xdfff

// Test if the wide character is a high surrogate
static bool IsHighSurrogate(const CHAR16_T c)
{
    return (c & 0xFC00) == HIGH_SURROGATE_START;
}

// Test if the wide character is a low surrogate
static bool IsLowSurrogate(const CHAR16_T c)
{
    return (c & 0xFC00) == LOW_SURROGATE_START;
}

// Test if the wide character is a surrogate half
static bool IsSurrogate(const CHAR16_T c)
{
    return (c & 0xF800) == HIGH_SURROGATE_START;
}

static size_t minipal_wcslen(const CHAR16_T* str)
{
    size_t nChar = 0;
    while (*str++) nChar++;
    return nChar;
}

#define ContractAssert(cond)             \
    if (!(cond))                         \
    {                                    \
        errno = ERROR_INVALID_PARAMETER; \
        return 0;                        \
    }

#define ContractAssertVoid(cond)         \
    if (!(cond))                         \
    {                                    \
        errno = ERROR_INVALID_PARAMETER; \
        return;                          \
    }

#define ContractAssertReset(cond, f, a)  \
    if (!(cond))                         \
    {                                    \
        errno = ERROR_INVALID_PARAMETER; \
        if (a) f(a);                     \
        return 0;                        \
    }

#define RETURN_ON_ERROR(f, a) \
    if (errno)                \
    {                         \
        if (a) f(a);          \
        return 0;             \
    }

typedef struct
{
    // Store our default string
    unsigned char* byteStart;
    CHAR16_T* charEnd;
    const CHAR16_T strDefault[2];
    int strDefaultLength;
    int fallbackCount;
    int fallbackIndex;
} DecoderBuffer;

static CHAR16_T DecoderReplacementFallbackBuffer_GetNextChar(DecoderBuffer* self)
{
    // We want it to get < 0 because == 0 means that the current/last character is a fallback
    // and we need to detect recursion.  We could have a flag but we already have this counter.
    self->fallbackCount--;
    self->fallbackIndex++;

    // Do we have anything left? 0 is now last fallback char, negative is nothing left
    if (self->fallbackCount < 0)
        return '\0';

    // Need to get it out of the buffer.
    // Make sure it didn't wrap from the fast count-- path
    if (self->fallbackCount == INT_MAX)
    {
        self->fallbackCount = -1;
        return '\0';
    }

    // Now make sure its in the expected range
    ContractAssert(self->fallbackIndex < self->strDefaultLength && self->fallbackIndex >= 0)

    return self->strDefault[self->fallbackIndex];
}

// Fallback Methods
static bool DecoderReplacementFallbackBuffer_Fallback(DecoderBuffer* self, unsigned char bytesUnknown[], int index, int size)
{
    // We expect no previous fallback in our buffer
    // We can't call recursively but others might (note, we don't test on last char!!!)
    ContractAssert(self->fallbackCount < 1)

    // Go ahead and get our fallback
    if (self->strDefaultLength == 0)
        return false;

    self->fallbackCount = self->strDefaultLength;
    self->fallbackIndex = -1;

    return true;
}

// This version just counts the fallback and doesn't actually copy anything.
static int DecoderReplacementFallbackBuffer_InternalFallback(DecoderBuffer* self, unsigned char bytes[], unsigned char* pBytes, int size)
    // Right now this has both bytes and bytes[], since we might have extra bytes, hence the
    // array, and we might need the index, hence the byte*
{
    // return our replacement string Length
    return self->strDefaultLength;
}

// Fallback the current byte by sticking it into the remaining char buffer.
// This can only be called by our encodings (other have to use the public fallback methods), so
// we can use our DecoderNLS here too (except we don't).
// Returns true if we are successful, false if we can't fallback the character (no buffer space)
// So caller needs to throw buffer space if return false.
// Right now this has both bytes and bytes[], since we might have extra bytes, hence the
// array, and we might need the index, hence the byte*
// Don't touch ref chars unless we succeed
static bool DecoderReplacementFallbackBuffer_InternalFallback_Copy(DecoderBuffer* self, unsigned char bytes[], unsigned char* pBytes, CHAR16_T** chars, int size)
{
    ContractAssert(self->byteStart != NULL)

    bool fallbackResult = DecoderReplacementFallbackBuffer_Fallback(self, bytes, (int)(pBytes - self->byteStart - size), size);
    if (errno) return false;

    // See if there's a fallback character and we have an output buffer then copy our string.
    if (fallbackResult)
    {
        // Copy the chars to our output
        CHAR16_T ch;
        CHAR16_T* charTemp = *chars;
        bool bHighSurrogate = false;
        while ((ch = DecoderReplacementFallbackBuffer_GetNextChar(self)) != 0)
        {
            // Make sure no mixed up surrogates
            if (IsSurrogate(ch))
            {
                if (IsHighSurrogate(ch))
                {
                    // High Surrogate
                    ContractAssert(!bHighSurrogate)
                    bHighSurrogate = true;
                }
                else
                {
                    // Low surrogate
                    ContractAssert(bHighSurrogate)
                    bHighSurrogate = false;
                }
            }

            if (charTemp >= self->charEnd)
            {
                // No buffer space
                return false;
            }

            *(charTemp++) = ch;
        }

        // Need to make sure that bHighSurrogate isn't true
        ContractAssert(!bHighSurrogate)

        // Now we aren't going to be false, so its OK to update chars
        *chars = charTemp;
    }

    return true;
}

// How many characters left to output?
static int DecoderReplacementFallbackBuffer_GetRemaining(DecoderBuffer* self)
{
    // Our count is 0 for 1 character left.
    return (self->fallbackCount < 0) ? 0 : self->fallbackCount;
}

// Clear the buffer
static void DecoderReplacementFallbackBuffer_Reset(DecoderBuffer* self)
{
    self->fallbackCount = -1;
    self->fallbackIndex = -1;
    self->byteStart = NULL;
}

// Set the above values
static void DecoderBuffer_InternalInitialize(DecoderBuffer* self, unsigned char* byteStart, CHAR16_T* charEnd)
{
    self->byteStart = byteStart;
    self->charEnd = charEnd;
}

typedef struct
{
    const CHAR16_T strDefault[3];
    int strDefaultLength;
    CHAR16_T* charStart;
    CHAR16_T* charEnd;
    bool setEncoder;
    bool bUsedEncoder;
    bool bFallingBack;
    int iRecursionCount;
    int fallbackCount;
    int fallbackIndex;
} EncoderBuffer;

#define MAX_RECURSION 250

// Not sure if this should be public or not.
// Clear the buffer
static void EncoderReplacementFallbackBuffer_Reset(EncoderBuffer* self)
{
    self->fallbackCount = -1;
    self->fallbackIndex = 0;
    self->charStart = NULL;
    self->bFallingBack = false;
}

// Set the above values
// This can't be part of the constructor because EncoderFallbacks would have to know how to implement these.
static void EncoderReplacementFallbackBuffer_InternalInitialize(EncoderBuffer* self, CHAR16_T* charStart, CHAR16_T* charEnd, bool setEncoder)
{
    self->charStart = charStart;
    self->charEnd = charEnd;
    self->setEncoder = setEncoder;
    self->bUsedEncoder = false;
    self->bFallingBack = false;
    self->iRecursionCount = 0;
}

static CHAR16_T EncoderReplacementFallbackBuffer_InternalGetNextChar(EncoderBuffer* self)
{
    // We want it to get < 0 because == 0 means that the current/last character is a fallback
    // and we need to detect recursion.  We could have a flag but we already have this counter.
    self->fallbackCount--;
    self->fallbackIndex++;

    // Do we have anything left? 0 is now last fallback char, negative is nothing left
    if (self->fallbackCount < 0)
        return '\0';

    // Need to get it out of the buffer.
    // Make sure it didn't wrap from the fast count-- path
    if (self->fallbackCount == INT_MAX)
    {
        self->fallbackCount = -1;
        return '\0';
    }

    // Now make sure its in the expected range
    ContractAssert(self->fallbackIndex < self->strDefaultLength && self->fallbackIndex >= 0)

    CHAR16_T ch = self->strDefault[self->fallbackIndex];
    self->bFallingBack = (ch != 0);
    if (ch == 0) self->iRecursionCount = 0;
    return ch;
}

// Fallback Methods
static bool EncoderReplacementFallbackBuffer_Fallback(EncoderBuffer* self, CHAR16_T charUnknown, int index)
{
    // If we had a buffer already we're being recursive, throw, it's probably at the suspect
    // character in our array.
    ContractAssert(self->fallbackCount < 1)

    // Go ahead and get our fallback
    // Divide by 2 because we aren't a surrogate pair
    self->fallbackCount = self->strDefaultLength / 2;
    self->fallbackIndex = -1;

    return self->fallbackCount != 0;
}

static bool EncoderReplacementFallbackBuffer_Fallback_Unknown(EncoderBuffer* self, CHAR16_T charUnknownHigh, CHAR16_T charUnknownLow, int index)
{
    // Double check input surrogate pair
    ContractAssert(IsHighSurrogate(charUnknownHigh))
    ContractAssert(IsLowSurrogate(charUnknownLow))

    // If we had a buffer already we're being recursive, throw, it's probably at the suspect
    // character in our array.
    ContractAssert(self->fallbackCount < 1)

    // Go ahead and get our fallback
    self->fallbackCount = self->strDefaultLength;
    self->fallbackIndex = -1;

    return self->fallbackCount != 0;
}

// Fallback the current character using the remaining buffer and encoder if necessary
// This can only be called by our encodings (other have to use the public fallback methods), so
// we can use our EncoderNLS here too.
// setEncoder is true if we're calling from a GetBytes method, false if we're calling from a GetByteCount
//
// Note that this could also change the contents of self->buffer.encoderBuffer, which is the same
// object that the caller is using, so the caller could mess up the encoder for us
// if they aren't careful.
static bool EncoderReplacementFallbackBuffer_InternalFallback(EncoderBuffer* self, CHAR16_T ch, CHAR16_T** chars)
{
    // Shouldn't have null charStart
    ContractAssert(self->charStart != NULL)

    // Get our index, remember chars was preincremented to point at next char, so have to -1
    int index = (int)(*chars - self->charStart) - 1;

    // See if it was a high surrogate
    if (IsHighSurrogate(ch))
    {
        // See if there's a low surrogate to go with it
        if (*chars >= self->charEnd)
        {
            // Nothing left in input buffer
            // No input, return 0
        }
        else
        {
            // Might have a low surrogate
            CHAR16_T cNext = **chars;
            if (IsLowSurrogate(cNext))
            {
                // If already falling back then fail
                ContractAssert(!self->bFallingBack || self->iRecursionCount++ <= MAX_RECURSION)

                // Next is a surrogate, add it as surrogate pair, and increment chars
                (*chars)++;
                self->bFallingBack = EncoderReplacementFallbackBuffer_Fallback_Unknown(self, ch, cNext, index);
                return self->bFallingBack;
            }

            // Next isn't a low surrogate, just fallback the high surrogate
        }
    }

    // If already falling back then fail
    ContractAssert(!self->bFallingBack || self->iRecursionCount++ <= MAX_RECURSION)

    // Fall back our char
    self->bFallingBack = EncoderReplacementFallbackBuffer_Fallback(self, ch, index);

    return self->bFallingBack;
}

static bool EncoderReplacementFallbackBuffer_MovePrevious(EncoderBuffer* self)
{
    // Back up one, only if we just processed the last character (or earlier)
    if (self->fallbackCount >= -1 && self->fallbackIndex >= 0)
    {
        self->fallbackIndex--;
        self->fallbackCount++;
        return true;
    }

    // Return false 'cause we couldn't do it.
    return false;
}

// How many characters left to output?
static int EncoderReplacementFallbackBuffer_GetRemaining(EncoderBuffer* self)
{
    // Our count is 0 for 1 character left.
    return (self->fallbackCount < 0) ? 0 : self->fallbackCount;
}

typedef struct
{
    union
    {
        DecoderBuffer decoderBuffer;
        EncoderBuffer encoderBuffer;
    } buffer;

    bool useFallback;

#if BIGENDIAN
    bool treatAsLE;
#endif
} UTF8Encoding;

// These are bitmasks used to maintain the state in the decoder. They occupy the higher bits
// while the actual character is being built in the lower bits. They are shifted together
// with the actual bits of the character.

// bits 30 & 31 are used for pending bits fixup
#define FinalByte (1 << 29)
#define SupplimentarySeq (1 << 28)
#define ThreeByteSeq (1 << 27)

static bool InRange(int c, int begin, int end)
{
    return begin <= c && c <= end;
}

static int GetBytesUnknown(unsigned char* pSrc, int ch, unsigned char* bytesUnknown)
{
    int size;

    // See if it was a plain char
    // (have to check >= 0 because we have all sorts of weird bit flags)
    if (ch < 0x100 && ch >= 0)
    {
        pSrc--;
        bytesUnknown[0] = (unsigned char)ch;
        size =  1;
    }
    // See if its an unfinished 2 byte sequence
    else if ((ch & (SupplimentarySeq | ThreeByteSeq)) == 0)
    {
        pSrc--;
        bytesUnknown[0] = (unsigned char)((ch & 0x1F) | 0xc0);
        size = 1;
    }
    // So now we're either 2nd byte of 3 or 4 byte sequence or
    // we hit a non-trail byte or we ran out of space for 3rd byte of 4 byte sequence
    // 1st check if its a 4 byte sequence
    else if ((ch & SupplimentarySeq) != 0)
    {
        //  3rd byte of 4 byte sequence?
        if ((ch & (FinalByte >> 6)) != 0)
        {
            // 3rd byte of 4 byte sequence
            pSrc -= 3;
            bytesUnknown[0] = (unsigned char)(((ch >> 12) & 0x07) | 0xF0);
            bytesUnknown[1] = (unsigned char)(((ch >> 6) & 0x3F) | 0x80);
            bytesUnknown[2] = (unsigned char)(((ch)& 0x3F) | 0x80);
            size = 3;
        }
        else if ((ch & (FinalByte >> 12)) != 0)
        {
            // 2nd byte of a 4 byte sequence
            pSrc -= 2;
            bytesUnknown[0] = (unsigned char)(((ch >> 6) & 0x07) | 0xF0);
            bytesUnknown[1] = (unsigned char)(((ch)& 0x3F) | 0x80);
            size = 2;
        }
        else
        {
            // 4th byte of a 4 byte sequence
            pSrc--;
            bytesUnknown[0] = (unsigned char)(((ch)& 0x07) | 0xF0);
            size = 1;
        }
    }
    else
    {
        // 2nd byte of 3 byte sequence?
        if ((ch & (FinalByte >> 6)) != 0)
        {
            // So its 2nd byte of a 3 byte sequence
            pSrc -= 2;
            bytesUnknown[0] = (unsigned char)(((ch >> 6) & 0x0F) | 0xE0);
            bytesUnknown[1] = (unsigned char)(((ch)& 0x3F) | 0x80);
            size = 2;
        }
        else
        {
            // 1st byte of a 3 byte sequence
            pSrc--;
            bytesUnknown[0] = (unsigned char)(((ch)& 0x0F) | 0xE0);
            size = 1;
        }
    }

    return size;
}

// During GetChars we had an invalid byte sequence
// pSrc is backed up to the start of the bad sequence if we didn't have room to
// fall it back.  Otherwise pSrc remains where it is.
static bool FallbackInvalidByteSequence_Copy(UTF8Encoding* self, unsigned char** pSrc, int ch, CHAR16_T** pTarget)
{
    ContractAssert(self->useFallback)

    // Get our byte[]
    unsigned char* pStart = *pSrc;
    unsigned char bytesUnknown[3];
    int size = GetBytesUnknown(pStart, ch, bytesUnknown);
    bool fallbackResult = DecoderReplacementFallbackBuffer_InternalFallback_Copy(&self->buffer.decoderBuffer, bytesUnknown, *pSrc, pTarget, size);
    RETURN_ON_ERROR(DecoderReplacementFallbackBuffer_Reset, &self->buffer.decoderBuffer)

    // Do the actual fallback
    if (!fallbackResult)
    {
        // Oops, it failed, back up to pStart
        *pSrc = pStart;
        return false;
    }

    // It worked
    return true;
}

static int FallbackInvalidByteSequence(UTF8Encoding* self, unsigned char* pSrc, int ch)
{
    ContractAssert(self->useFallback)

    // Get our byte[]
    unsigned char bytesUnknown[3];
    int size = GetBytesUnknown(pSrc, ch, bytesUnknown);

    // Do the actual fallback
    int count = DecoderReplacementFallbackBuffer_InternalFallback(&self->buffer.decoderBuffer, bytesUnknown, pSrc, size);

    // # of fallback chars expected.
    // Note that we only get here for "long" sequences, and have already unreserved
    // the count that we prereserved for the input bytes
    return count;
}

static int GetCharCount(UTF8Encoding* self, unsigned char* bytes, int count)
{
    ContractAssert(bytes != NULL)
    ContractAssert(count >= 0)

    // Initialize stuff
    unsigned char *pSrc = bytes;
    unsigned char *pEnd = pSrc + count;
    int availableBytes, chc;

    // Start by assuming we have as many as count, charCount always includes the adjustment
    // for the character being decoded
    int charCount = count;
    int ch = 0;
    bool fallbackUsed = false;

    while (true)
    {
        // SLOWLOOP: does all range checks, handles all special cases, but it is slow
        if (pSrc >= pEnd) {
            break;
        }

        // read next byte. The JIT optimization seems to be getting confused when
        // compiling "ch = *pSrc++;", so rather use "ch = *pSrc; pSrc++;" instead
        int cha = *pSrc;

        if (ch == 0) {
            // no pending bits
            goto ReadChar;
        }

        pSrc++;

        // we are expecting to see trailing bytes like 10vvvvvv
        if ((cha & 0xC0) != 0x80) {
            // This can be a valid starting byte for another UTF8 byte sequence, so let's put
            // the current byte back, and try to see if this is a valid byte for another UTF8 byte sequence
            pSrc--;
            charCount += (ch >> 30);
            goto InvalidByteSequence;
        }

        // fold in the new byte
        ch = (ch << 6) | (cha & 0x3F);

        if ((ch & FinalByte) == 0) {
            ContractAssertReset((ch & (SupplimentarySeq | ThreeByteSeq)) != 0, DecoderReplacementFallbackBuffer_Reset, &self->buffer.decoderBuffer)

            if ((ch & SupplimentarySeq) != 0) {
                if ((ch & (FinalByte >> 6)) != 0) {
                    // this is 3rd byte (of 4 byte supplimentary) - nothing to do
                    continue;
                }

                // 2nd byte, check for non-shortest form of supplimentary char and the valid
                // supplimentary characters in range 0x010000 - 0x10FFFF at the same time
                if (!InRange(ch & 0x1F0, 0x10, 0x100)) {
                    goto InvalidByteSequence;
                }
            }
            else {
                // Must be 2nd byte of a 3-byte sequence
                // check for non-shortest form of 3 byte seq
                if ((ch & (0x1F << 5)) == 0 ||                  // non-shortest form
                    (ch & (0xF800 >> 6)) == (0xD800 >> 6))     // illegal individually encoded surrogate
                {
                    goto InvalidByteSequence;
                }
            }
            continue;
        }

        // ready to punch

        // adjust for surrogates in non-shortest form
        if ((ch & (SupplimentarySeq | 0x1F0000)) == SupplimentarySeq) {
            charCount--;
        }
        goto EncodeChar;

    InvalidByteSequence:
        // this code fragment should be close to the gotos referencing it
        // Have to do fallback for invalid bytes
        if (!fallbackUsed)
        {
            fallbackUsed = true;
            if (self->useFallback) DecoderBuffer_InternalInitialize(&self->buffer.decoderBuffer, bytes, NULL);
        }
        charCount += FallbackInvalidByteSequence(self, pSrc, ch);

        ch = 0;
        continue;

    ReadChar:
        ch = *pSrc;
        pSrc++;

    ProcessChar:
        if (ch > 0x7F) {
            // If its > 0x7F, its start of a new multi-byte sequence

            // Long sequence, so unreserve our char.
            charCount--;

            // bit 6 has to be non-zero for start of multibyte chars.
            if ((ch & 0x40) == 0) {
                // Unexpected trail byte
                goto InvalidByteSequence;
            }

            // start a new long code
            if ((ch & 0x20) != 0) {
                if ((ch & 0x10) != 0) {
                    // 4 byte encoding - supplimentary character (2 surrogates)

                    ch &= 0x0F;

                    // check that bit 4 is zero and the valid supplimentary character
                    // range 0x000000 - 0x10FFFF at the same time
                    if (ch > 0x04) {
                        ch |= 0xf0;
                        goto InvalidByteSequence;
                    }

                    // Add bit flags so that when we check new characters & rotate we'll be flagged correctly.
                    // Final byte flag, count fix if we don't make final byte & supplimentary sequence flag.
                    ch |= (FinalByte >> 3 * 6) |  // Final byte is 3 more bytes from now
                        (1 << 30) |           // If it dies on next byte we'll need an extra char
                        (3 << (30 - 2 * 6)) |     // If it dies on last byte we'll need to subtract a char
                        (SupplimentarySeq) | (SupplimentarySeq >> 6) |
                        (SupplimentarySeq >> 2 * 6) | (SupplimentarySeq >> 3 * 6);

                    // Our character count will be 2 characters for these 4 bytes, so subtract another char
                    charCount--;
                }
                else {
                    // 3 byte encoding
                    // Add bit flags so that when we check new characters & rotate we'll be flagged correctly.
                    ch = (ch & 0x0F) | ((FinalByte >> 2 * 6) | (1 << 30) |
                        (ThreeByteSeq) | (ThreeByteSeq >> 6) | (ThreeByteSeq >> 2 * 6));

                    // We'll expect 1 character for these 3 bytes, so subtract another char.
                    charCount--;
                }
            }
            else {
                // 2 byte encoding

                ch &= 0x1F;

                // check for non-shortest form
                if (ch <= 1) {
                    ch |= 0xc0;
                    goto InvalidByteSequence;
                }

                // Add bit flags so we'll be flagged correctly
                ch |= (FinalByte >> 6);
            }
            continue;
        }

    EncodeChar:

#ifdef FASTLOOP
        availableBytes = pEnd - pSrc;

        // don't fall into the fast decoding loop if we don't have enough bytes
        if (availableBytes <= 13) {
            // try to get over the remainder of the ascii characters fast though
        unsigned char* pLocalEnd = pEnd; // hint to get pLocalEnd enregistered
            while (pSrc < pLocalEnd) {
                ch = *pSrc;
                pSrc++;

                if (ch > 0x7F)
                    goto ProcessChar;
            }
            // we are done
            ch = 0;
            break;
        }

        // To compute the upper bound, assume that all characters are ASCII characters at this point,
        //  the boundary will be decreased for every non-ASCII character we encounter
        // Also, we need 7 chars reserve for the unrolled ansi decoding loop and for decoding of multibyte sequences
        unsigned char *pStop = pSrc + availableBytes - 7;

        while (pSrc < pStop) {
            ch = *pSrc;
            pSrc++;

            if (ch > 0x7F) {
                goto LongCode;
            }

            // get pSrc 2-byte aligned
            if (((size_t)pSrc & 0x1) != 0) {
                ch = *pSrc;
                pSrc++;
                if (ch > 0x7F) {
                    goto LongCode;
                }
            }

            // get pSrc 4-byte aligned
            if (((size_t)pSrc & 0x2) != 0) {
                ch = *(unsigned short*)pSrc;
                if ((ch & 0x8080) != 0) {
                    goto LongCodeWithMask16;
                }
                pSrc += 2;
            }


            // Run 8 + 8 characters at a time!
            while (pSrc < pStop) {
                ch = *(int*)pSrc;
                int chb = *(int*)(pSrc + 4);
                if (((ch | chb) & (int)0x80808080) != 0) {
                    goto LongCodeWithMask32;
                }
                pSrc += 8;

                // This is a really small loop - unroll it
                if (pSrc >= pStop)
                    break;

                ch = *(int*)pSrc;
                chb = *(int*)(pSrc + 4);
                if (((ch | chb) & (int)0x80808080) != 0) {
                    goto LongCodeWithMask32;
                }
                pSrc += 8;
            }
            break;

        LongCodeWithMask32 :
#if BIGENDIAN
        // be careful about the sign extension
        if (!treatAsLE) ch = (int)(((unsigned int)ch) >> 16);
        else
#else
            ch &= 0xFF;
#endif

        LongCodeWithMask16:
#if BIGENDIAN
        if (!treatAsLE) ch = (int)(((unsigned int)ch) >> 8);
        else
#else
            ch &= 0xFF;
#endif

        pSrc++;
        if (ch <= 0x7F) {
            continue;
        }

        LongCode:
            chc = *pSrc;
            pSrc++;

            if (
                // bit 6 has to be zero
                (ch & 0x40) == 0 ||
                // we are expecting to see trailing bytes like 10vvvvvv
                (chc & 0xC0) != 0x80)
            {
                goto BadLongCode;
            }

            chc &= 0x3F;

            // start a new long code
            if ((ch & 0x20) != 0) {

                // fold the first two bytes together
                chc |= (ch & 0x0F) << 6;

                if ((ch & 0x10) != 0) {
                    // 4 byte encoding - surrogate
                    ch = *pSrc;
                    if (
                        // check that bit 4 is zero, the non-shortest form of surrogate
                        // and the valid surrogate range 0x000000 - 0x10FFFF at the same time
                        !InRange(chc >> 4, 0x01, 0x10) ||
                        // we are expecting to see trailing bytes like 10vvvvvv
                        (ch & 0xC0) != 0x80)
                    {
                        goto BadLongCode;
                    }

                    chc = (chc << 6) | (ch & 0x3F);

                    ch = *(pSrc + 1);
                    // we are expecting to see trailing bytes like 10vvvvvv
                    if ((ch & 0xC0) != 0x80) {
                        goto BadLongCode;
                    }
                    pSrc += 2;

                    // extra byte
                    charCount--;
                }
                else {
                    // 3 byte encoding
                    ch = *pSrc;
                    if (
                        // check for non-shortest form of 3 byte seq
                        (chc & (0x1F << 5)) == 0 ||
                        // Can't have surrogates here.
                        (chc & (0xF800 >> 6)) == (0xD800 >> 6) ||
                        // we are expecting to see trailing bytes like 10vvvvvv
                        (ch & 0xC0) != 0x80)
                    {
                        goto BadLongCode;
                    }
                    pSrc++;

                    // extra byte
                    charCount--;
                }
            }
            else {
                // 2 byte encoding

                // check for non-shortest form
                if ((ch & 0x1E) == 0) {
                    goto BadLongCode;
                }
            }

            // extra byte
            charCount--;
        }
#endif // FASTLOOP

        // no pending bits at this point
        ch = 0;
        continue;

    BadLongCode:
        pSrc -= 2;
        ch = 0;
        continue;
    }

    // May have a problem if we have to flush
    if (ch != 0)
    {
        // We were already adjusting for these, so need to unadjust
        charCount += (ch >> 30);
        // Have to do fallback for invalid bytes
        if (!fallbackUsed)
        {
            fallbackUsed = true;
            if (self->useFallback) DecoderBuffer_InternalInitialize(&self->buffer.decoderBuffer, bytes, NULL);
        }
        charCount += FallbackInvalidByteSequence(self, pSrc, ch);
    }

    // Shouldn't have anything in fallback buffer for GetCharCount
    // (don't have to check m_throwOnOverflow for count)
    ContractAssertReset(!fallbackUsed || !self->useFallback || DecoderReplacementFallbackBuffer_GetRemaining(&self->buffer.decoderBuffer) == 0,
        DecoderReplacementFallbackBuffer_Reset, &self->buffer.decoderBuffer)

    return charCount;
}

static int GetChars(UTF8Encoding* self, unsigned char* bytes, int byteCount, CHAR16_T* chars, int charCount)
{
    ContractAssert(chars != NULL)
    ContractAssert(byteCount >= 0)
    ContractAssert(charCount >= 0)
    ContractAssert(bytes != NULL)

    unsigned char *pSrc = bytes;
    CHAR16_T *pTarget = chars;

    unsigned char *pEnd = pSrc + byteCount;
    CHAR16_T *pAllocatedBufferEnd = pTarget + charCount;

    int ch = 0;
    int chc;

    bool fallbackUsed = false;

    while (true)
    {
        // SLOWLOOP: does all range checks, handles all special cases, but it is slow

        if (pSrc >= pEnd) {
            break;
        }

        // read next byte. The JIT optimization seems to be getting confused when
        // compiling "ch = *pSrc++;", so rather use "ch = *pSrc; pSrc++;" instead
        int cha = *pSrc;

        if (ch == 0) {
            // no pending bits
            goto ReadChar;
        }

        pSrc++;

        // we are expecting to see trailing bytes like 10vvvvvv
        if ((cha & 0xC0) != 0x80) {
            // This can be a valid starting byte for another UTF8 byte sequence, so let's put
            // the current byte back, and try to see if this is a valid byte for another UTF8 byte sequence
            pSrc--;
            goto InvalidByteSequence;
        }

        // fold in the new byte
        ch = (ch << 6) | (cha & 0x3F);

        if ((ch & FinalByte) == 0) {
            // Not at last byte yet
            ContractAssertReset((ch & (SupplimentarySeq | ThreeByteSeq)) != 0, DecoderReplacementFallbackBuffer_Reset, &self->buffer.decoderBuffer)

            if ((ch & SupplimentarySeq) != 0) {
                // Its a 4-byte supplimentary sequence
                if ((ch & (FinalByte >> 6)) != 0) {
                    // this is 3rd byte of 4 byte sequence - nothing to do
                    continue;
                }

                // 2nd byte of 4 bytes
                // check for non-shortest form of surrogate and the valid surrogate
                // range 0x000000 - 0x10FFFF at the same time
                if (!InRange(ch & 0x1F0, 0x10, 0x100)) {
                    goto InvalidByteSequence;
                }
            }
            else {
                // Must be 2nd byte of a 3-byte sequence
                // check for non-shortest form of 3 byte seq
                if ((ch & (0x1F << 5)) == 0 ||                  // non-shortest form
                    (ch & (0xF800 >> 6)) == (0xD800 >> 6))     // illegal individually encoded surrogate
                {
                    goto InvalidByteSequence;
                }
            }
            continue;
        }

        // ready to punch

        // surrogate in shortest form?
        // Might be possible to get rid of this?  Already did non-shortest check for 4-byte sequence when reading 2nd byte?
        if ((ch & (SupplimentarySeq | 0x1F0000)) > SupplimentarySeq) {
            // let the range check for the second char throw the exception
            if (pTarget < pAllocatedBufferEnd) {
                *pTarget = (CHAR16_T)(((ch >> 10) & 0x7FF) +
                    (short)((HIGH_SURROGATE_START - (0x10000 >> 10))));
                pTarget++;

                ch = (ch & 0x3FF) +
                    (int)(LOW_SURROGATE_START);
            }
        }

        goto EncodeChar;

    InvalidByteSequence:
        // this code fragment should be close to the gotos referencing it
        // Have to do fallback for invalid bytes
        if (!fallbackUsed)
        {
            fallbackUsed = true;
            if (self->useFallback) DecoderBuffer_InternalInitialize(&self->buffer.decoderBuffer, bytes, pAllocatedBufferEnd);
        }

        // That'll back us up the appropriate # of bytes if we didn't get anywhere
        if (!FallbackInvalidByteSequence_Copy(self, &pSrc, ch, &pTarget))
        {
            // Ran out of buffer space
            // Need to throw an exception?
            ContractAssertReset(pSrc >= bytes || pTarget == chars, DecoderReplacementFallbackBuffer_Reset, &self->buffer.decoderBuffer)
            if (self->useFallback) DecoderReplacementFallbackBuffer_Reset(&self->buffer.decoderBuffer);
            if (pTarget == chars)
            {
                errno = ERROR_INSUFFICIENT_BUFFER;
                return 0;
            }
            ch = 0;
            break;
        }
        ContractAssert(pSrc >= bytes)
        ch = 0;
        continue;

    ReadChar:
        ch = *pSrc;
        pSrc++;

    ProcessChar:
        if (ch > 0x7F) {
            // If its > 0x7F, its start of a new multi-byte sequence

            // bit 6 has to be non-zero
            if ((ch & 0x40) == 0) {
                goto InvalidByteSequence;
            }

            // start a new long code
            if ((ch & 0x20) != 0) {
                if ((ch & 0x10) != 0) {
                    // 4 byte encoding - supplimentary character (2 surrogates)

                    ch &= 0x0F;

                    // check that bit 4 is zero and the valid supplimentary character
                    // range 0x000000 - 0x10FFFF at the same time
                    if (ch > 0x04) {
                        ch |= 0xf0;
                        goto InvalidByteSequence;
                    }

                    ch |= (FinalByte >> 3 * 6) | (1 << 30) | (3 << (30 - 2 * 6)) |
                        (SupplimentarySeq) | (SupplimentarySeq >> 6) |
                        (SupplimentarySeq >> 2 * 6) | (SupplimentarySeq >> 3 * 6);
                }
                else {
                    // 3 byte encoding
                    ch = (ch & 0x0F) | ((FinalByte >> 2 * 6) | (1 << 30) |
                        (ThreeByteSeq) | (ThreeByteSeq >> 6) | (ThreeByteSeq >> 2 * 6));
                }
            }
            else {
                // 2 byte encoding

                ch &= 0x1F;

                // check for non-shortest form
                if (ch <= 1) {
                    ch |= 0xc0;
                    goto InvalidByteSequence;
                }

                ch |= (FinalByte >> 6);
            }
            continue;
        }

    EncodeChar:
        // write the pending character
        if (pTarget >= pAllocatedBufferEnd)
        {
            // Fix chars so we make sure to throw if we didn't output anything
            ch &= 0x1fffff;
            if (ch > 0x7f)
            {
                if (ch > 0x7ff)
                {
                    if (ch >= LOW_SURROGATE_START &&
                        ch <= LOW_SURROGATE_END)
                    {
                        pSrc--;     // It was 4 bytes
                        pTarget--;  // 1 was stored already, but we can't remember 1/2, so back up
                    }
                    else if (ch > 0xffff)
                    {
                        pSrc--;     // It was 4 bytes, nothing was stored
                    }
                    pSrc--;         // It was at least 3 bytes
                }
                pSrc--;             // It was at least 2 bytes
            }
            pSrc--;

            // Throw that we don't have enough room (pSrc could be < chars if we had started to process
            // a 4 byte sequence already)
            ContractAssert(pSrc >= bytes || pTarget == chars)
            if (pTarget == chars)
            {
                errno = ERROR_INSUFFICIENT_BUFFER;
                return 0;
            }

            // Don't store ch in decoder, we already backed up to its start
            ch = 0;

            // Didn't throw, just use this buffer size.
            break;
        }
        *pTarget = (CHAR16_T)ch;
        pTarget++;

#ifdef FASTLOOP
        int availableChars = pAllocatedBufferEnd - pTarget;
        int availableBytes = pEnd - pSrc;

        // don't fall into the fast decoding loop if we don't have enough bytes
        // Test for availableChars is done because pStop would be <= pTarget.
        if (availableBytes <= 13) {
            // we may need as many as 1 character per byte
            if (availableChars < availableBytes) {
                // not enough output room.  no pending bits at this point
                ch = 0;
                continue;
            }

            // try to get over the remainder of the ascii characters fast though
            unsigned char* pLocalEnd = pEnd; // hint to get pLocalEnd enregistered
            while (pSrc < pLocalEnd) {
                ch = *pSrc;
                pSrc++;

                if (ch > 0x7F)
                    goto ProcessChar;

                *pTarget = (CHAR16_T)ch;
                pTarget++;
            }
            // we are done
            ch = 0;
            break;
        }

        // we may need as many as 1 character per byte, so reduce the byte count if necessary.
        // If availableChars is too small, pStop will be before pTarget and we won't do fast loop.
        if (availableChars < availableBytes) {
            availableBytes = availableChars;
        }

        // To compute the upper bound, assume that all characters are ASCII characters at this point,
        //  the boundary will be decreased for every non-ASCII character we encounter
        // Also, we need 7 chars reserve for the unrolled ansi decoding loop and for decoding of multibyte sequences
        CHAR16_T *pStop = pTarget + availableBytes - 7;

        while (pTarget < pStop) {
            ch = *pSrc;
            pSrc++;

            if (ch > 0x7F) {
                goto LongCode;
            }
            *pTarget = (CHAR16_T)ch;
            pTarget++;

            // get pSrc to be 2-byte aligned
            if ((((size_t)pSrc) & 0x1) != 0) {
                ch = *pSrc;
                pSrc++;
                if (ch > 0x7F) {
                    goto LongCode;
                }
                *pTarget = (CHAR16_T)ch;
                pTarget++;
            }

            // get pSrc to be 4-byte aligned
            if ((((size_t)pSrc) & 0x2) != 0) {
                ch = *(unsigned short*)pSrc;
                if ((ch & 0x8080) != 0) {
                    goto LongCodeWithMask16;
                }

                // Unfortunately, this is endianness sensitive
#if BIGENDIAN
                if (!treatAsLE)
                {
                    *pTarget = (CHAR16_T)((ch >> 8) & 0x7F);
                    pSrc += 2;
                    *(pTarget + 1) = (CHAR16_T)(ch & 0x7F);
                    pTarget += 2;
                }
                else
#else
                {
                    *pTarget = (CHAR16_T)(ch & 0x7F);
                    pSrc += 2;
                    *(pTarget + 1) = (CHAR16_T)((ch >> 8) & 0x7F);
                    pTarget += 2;
                }
#endif
            }

            // Run 8 characters at a time!
            while (pTarget < pStop) {
                ch = *(int*)pSrc;
                int chb = *(int*)(pSrc + 4);
                if (((ch | chb) & (int)0x80808080) != 0) {
                    goto LongCodeWithMask32;
                }

                // Unfortunately, this is endianness sensitive
#if BIGENDIAN
                if (!treatAsLE)
                {
                    *pTarget = (CHAR16_T)((ch >> 24) & 0x7F);
                    *(pTarget + 1) = (CHAR16_T)((ch >> 16) & 0x7F);
                    *(pTarget + 2) = (CHAR16_T)((ch >> 8) & 0x7F);
                    *(pTarget + 3) = (CHAR16_T)(ch & 0x7F);
                    pSrc += 8;
                    *(pTarget + 4) = (CHAR16_T)((chb >> 24) & 0x7F);
                    *(pTarget + 5) = (CHAR16_T)((chb >> 16) & 0x7F);
                    *(pTarget + 6) = (CHAR16_T)((chb >> 8) & 0x7F);
                    *(pTarget + 7) = (CHAR16_T)(chb & 0x7F);
                    pTarget += 8;
                }
                else
#else
                {
                    *pTarget = (CHAR16_T)(ch & 0x7F);
                    *(pTarget + 1) = (CHAR16_T)((ch >> 8) & 0x7F);
                    *(pTarget + 2) = (CHAR16_T)((ch >> 16) & 0x7F);
                    *(pTarget + 3) = (CHAR16_T)((ch >> 24) & 0x7F);
                    pSrc += 8;
                    *(pTarget + 4) = (CHAR16_T)(chb & 0x7F);
                    *(pTarget + 5) = (CHAR16_T)((chb >> 8) & 0x7F);
                    *(pTarget + 6) = (CHAR16_T)((chb >> 16) & 0x7F);
                    *(pTarget + 7) = (CHAR16_T)((chb >> 24) & 0x7F);
                    pTarget += 8;
                }
#endif
            }
            break;

            LongCodeWithMask32 :
#if BIGENDIAN
            // be careful about the sign extension
            if (!treatAsLE) ch = (int)(((unsigned int)ch) >> 16);
            else
#else
            ch &= 0xFF;
#endif

            LongCodeWithMask16:
#if BIGENDIAN
            if (!treatAsLE) ch = (int)(((unsigned int)ch) >> 8);
            else
#else
            ch &= 0xFF;
#endif

            pSrc++;
            if (ch <= 0x7F) {
                *pTarget = (CHAR16_T)ch;
                pTarget++;
                continue;
            }

        LongCode:
            chc = *pSrc;
            pSrc++;

            if (
                // bit 6 has to be zero
                (ch & 0x40) == 0 ||
                // we are expecting to see trailing bytes like 10vvvvvv
                (chc & 0xC0) != 0x80)
            {
                goto BadLongCode;
            }

            chc &= 0x3F;

            // start a new long code
            if ((ch & 0x20) != 0) {

                // fold the first two bytes together
                chc |= (ch & 0x0F) << 6;

                if ((ch & 0x10) != 0) {
                    // 4 byte encoding - surrogate
                    ch = *pSrc;
                    if (
                        // check that bit 4 is zero, the non-shortest form of surrogate
                        // and the valid surrogate range 0x000000 - 0x10FFFF at the same time
                        !InRange(chc >> 4, 0x01, 0x10) ||
                        // we are expecting to see trailing bytes like 10vvvvvv
                        (ch & 0xC0) != 0x80)
                    {
                        goto BadLongCode;
                    }

                    chc = (chc << 6) | (ch & 0x3F);

                    ch = *(pSrc + 1);
                    // we are expecting to see trailing bytes like 10vvvvvv
                    if ((ch & 0xC0) != 0x80) {
                        goto BadLongCode;
                    }
                    pSrc += 2;

                    ch = (chc << 6) | (ch & 0x3F);

                    *pTarget = (CHAR16_T)(((ch >> 10) & 0x7FF) +
                        (short)(HIGH_SURROGATE_START - (0x10000 >> 10)));
                    pTarget++;

                    ch = (ch & 0x3FF) +
                        (short)(LOW_SURROGATE_START);

                    // extra byte, we're already planning 2 chars for 2 of these bytes,
                    // but the big loop is testing the target against pStop, so we need
                    // to subtract 2 more or we risk overrunning the input.  Subtract
                    // one here and one below.
                    pStop--;
                }
                else {
                    // 3 byte encoding
                    ch = *pSrc;
                    if (
                        // check for non-shortest form of 3 byte seq
                        (chc & (0x1F << 5)) == 0 ||
                        // Can't have surrogates here.
                        (chc & (0xF800 >> 6)) == (0xD800 >> 6) ||
                        // we are expecting to see trailing bytes like 10vvvvvv
                        (ch & 0xC0) != 0x80)
                    {
                        goto BadLongCode;
                    }
                    pSrc++;

                    ch = (chc << 6) | (ch & 0x3F);

                    // extra byte, we're only expecting 1 char for each of these 3 bytes,
                    // but the loop is testing the target (not source) against pStop, so
                    // we need to subtract 2 more or we risk overrunning the input.
                    // Subtract 1 here and one more below
                    pStop--;
                }
            }
            else {
                // 2 byte encoding

                ch &= 0x1F;

                // check for non-shortest form
                if (ch <= 1) {
                    goto BadLongCode;
                }
                ch = (ch << 6) | chc;
            }

            *pTarget = (CHAR16_T)ch;
            pTarget++;

            // extra byte, we're only expecting 1 char for each of these 2 bytes,
            // but the loop is testing the target (not source) against pStop.
            // subtract an extra count from pStop so that we don't overrun the input.
            pStop--;
        }
#endif // FASTLOOP

        ContractAssert(pTarget <= pAllocatedBufferEnd)

        // no pending bits at this point
        ch = 0;
        continue;

    BadLongCode:
        pSrc -= 2;
        ch = 0;
        continue;
    }

    if (ch != 0)
    {
        // Have to do fallback for invalid bytes
        if (!fallbackUsed)
        {
            fallbackUsed = true;
            if (self->useFallback) DecoderBuffer_InternalInitialize(&self->buffer.decoderBuffer, bytes, NULL);
        }

        // This'll back us up the appropriate # of bytes if we didn't get anywhere
        if (!FallbackInvalidByteSequence(self, pSrc, ch))
        {
            ContractAssertReset(pSrc >= bytes || pTarget == chars, DecoderReplacementFallbackBuffer_Reset, &self->buffer.decoderBuffer)

            // Ran out of buffer space
            // Need to throw an exception?
            if (self->useFallback) DecoderReplacementFallbackBuffer_Reset(&self->buffer.decoderBuffer);
            if (pTarget == chars)
            {
                errno = ERROR_INSUFFICIENT_BUFFER;
                return 0;
            }
        }
        ContractAssertReset(pSrc >= bytes, DecoderReplacementFallbackBuffer_Reset, &self->buffer.decoderBuffer)
        ch = 0;
    }

    // Shouldn't have anything in fallback buffer for GetChars
    // (don't have to check m_throwOnOverflow for chars)
    ContractAssert(!fallbackUsed || DecoderReplacementFallbackBuffer_GetRemaining(&self->buffer.decoderBuffer) == 0)

    return pTarget - chars;
}

static int GetBytes(UTF8Encoding* self, CHAR16_T* chars, int charCount, unsigned char* bytes, int byteCount)
{
    ContractAssert(chars != NULL)
    ContractAssert(byteCount >= 0)
    ContractAssert(charCount >= 0)
    ContractAssert(bytes != NULL)

    // For fallback we may need a fallback buffer.
    // We wait to initialize it though in case we don't have any broken input unicode
    bool fallbackUsed = false;
    CHAR16_T *pSrc = chars;
    unsigned char *pTarget = bytes;

    CHAR16_T *pEnd = pSrc + charCount;
    unsigned char *pAllocatedBufferEnd = pTarget + byteCount;

    int ch = 0;
    int chd;

    // assume that JIT will enregister pSrc, pTarget and ch

    while (true) {
        // SLOWLOOP: does all range checks, handles all special cases, but it is slow

        if (pSrc >= pEnd) {

            if (ch == 0) {
                // Check if there's anything left to get out of the fallback buffer
                ch = fallbackUsed ? EncoderReplacementFallbackBuffer_InternalGetNextChar(&self->buffer.encoderBuffer) : 0;
                if (ch > 0) {
                    goto ProcessChar;
                }
            }
            else {
                // Case of leftover surrogates in the fallback buffer
                if (fallbackUsed && self->buffer.encoderBuffer.bFallingBack) {
                    ContractAssertReset(ch >= 0xD800 && ch <= 0xDBFF, EncoderReplacementFallbackBuffer_Reset, &self->buffer.encoderBuffer)

                    int cha = ch;

                    ch = EncoderReplacementFallbackBuffer_InternalGetNextChar(&self->buffer.encoderBuffer);

                    if (InRange(ch, LOW_SURROGATE_START, LOW_SURROGATE_END)) {
                        ch = ch + (cha << 10) + (0x10000 - LOW_SURROGATE_START - (HIGH_SURROGATE_START << 10));
                        goto EncodeChar;
                    }
                    else if (ch > 0){
                        goto ProcessChar;
                    }
                    else {
                        break;
                    }
                }
            }

            // attempt to encode the partial surrogate (will fail or ignore)
            if (ch > 0)
                goto EncodeChar;

            // We're done
            break;
        }

        if (ch > 0) {
            // We have a high surrogate left over from a previous loop.
            ContractAssertReset(ch >= 0xD800 && ch <= 0xDBFF, EncoderReplacementFallbackBuffer_Reset, &self->buffer.encoderBuffer);

            // use separate helper variables for local contexts so that the jit optimizations
            // won't get confused about the variable lifetimes
            int cha = *pSrc;

            // In previous byte, we encountered a high surrogate, so we are expecting a low surrogate here.
            // if (IsLowSurrogate(cha)) {
            if (InRange(cha, LOW_SURROGATE_START, LOW_SURROGATE_END)) {
                ch = cha + (ch << 10) +
                    (0x10000
                    - LOW_SURROGATE_START
                    - (HIGH_SURROGATE_START << 10));

                pSrc++;
            }
            // else ch is still high surrogate and encoding will fail

            // attempt to encode the surrogate or partial surrogate
            goto EncodeChar;
        }

        // If we've used a fallback, then we have to check for it
        if (fallbackUsed)
        {
            ch = EncoderReplacementFallbackBuffer_InternalGetNextChar(&self->buffer.encoderBuffer);
            if (ch > 0) goto ProcessChar;
        }

        // read next char. The JIT optimization seems to be getting confused when
        // compiling "ch = *pSrc++;", so rather use "ch = *pSrc; pSrc++;" instead
        ch = *pSrc;
        pSrc++;

    ProcessChar:
        if (InRange(ch, HIGH_SURROGATE_START, HIGH_SURROGATE_END)) {
            continue;
        }
        // either good char or partial surrogate

    EncodeChar:
        // throw exception on partial surrogate if necessary
        if (InRange(ch, HIGH_SURROGATE_START, LOW_SURROGATE_END))
        {
            // Lone surrogates aren't allowed, we have to do fallback for them
            // Have to make a fallback buffer if we don't have one
            if (!fallbackUsed)
            {
                // wait on fallbacks if we can
                // For fallback we may need a fallback buffer
                fallbackUsed = true;

                // Set our internal fallback interesting things.
                EncoderReplacementFallbackBuffer_InternalInitialize(&self->buffer.encoderBuffer, chars, pEnd, true);
            }

            // Do our fallback.  Actually we already know its a mixed up surrogate,
            // so the ref pSrc isn't gonna do anything.
            EncoderReplacementFallbackBuffer_InternalFallback(&self->buffer.encoderBuffer, (CHAR16_T)ch, &pSrc);
            RETURN_ON_ERROR(EncoderReplacementFallbackBuffer_Reset, &self->buffer.encoderBuffer)

            // Ignore it if we don't throw
            ch = 0;
            continue;
        }

        // Count bytes needed
        int bytesNeeded = 1;
        if (ch > 0x7F) {
            if (ch > 0x7FF) {
                if (ch > 0xFFFF) {
                    bytesNeeded++;  // 4 bytes (surrogate pair)
                }
                bytesNeeded++;      // 3 bytes (800-FFFF)
            }
            bytesNeeded++;          // 2 bytes (80-7FF)
        }

        if (pTarget > pAllocatedBufferEnd - bytesNeeded) {
            // Left over surrogate from last time will cause pSrc == chars, so we'll throw
            if (fallbackUsed && self->buffer.encoderBuffer.bFallingBack)
            {
                EncoderReplacementFallbackBuffer_MovePrevious(&self->buffer.encoderBuffer);              // Didn't use this fallback char
                if (ch > 0xFFFF)
                    EncoderReplacementFallbackBuffer_MovePrevious(&self->buffer.encoderBuffer);          // Was surrogate, didn't use 2nd part either
            }
            else
            {
                pSrc--;                                     // Didn't use this char
                if (ch > 0xFFFF)
                    pSrc--;                                 // Was surrogate, didn't use 2nd part either
            }
            ContractAssertReset(pSrc >= chars || pTarget == bytes, EncoderReplacementFallbackBuffer_Reset, &self->buffer.encoderBuffer);
            if (pTarget == bytes)  // Throw if we must
            {
                errno = ERROR_INSUFFICIENT_BUFFER;
                return 0;
            }
            ch = 0;                                         // Nothing left over (we backed up to start of pair if supplimentary)
            break;
        }

        if (ch <= 0x7F) {
            *pTarget = (unsigned char)ch;
        }
        else {
            // use separate helper variables for local contexts so that the jit optimizations
            // won't get confused about the variable lifetimes
            int chb;
            if (ch <= 0x7FF) {
                // 2 unsigned char encoding
                chb = (unsigned char)(0xC0 | (ch >> 6));
            }
            else
            {
                if (ch <= 0xFFFF) {
                    chb = (unsigned char)(0xE0 | (ch >> 12));
                }
                else
                {
                    *pTarget = (unsigned char)(0xF0 | (ch >> 18));
                    pTarget++;

                    chb = 0x80 | ((ch >> 12) & 0x3F);
                }
                *pTarget = (unsigned char)chb;
                pTarget++;

                chb = 0x80 | ((ch >> 6) & 0x3F);
            }
            *pTarget = (unsigned char)chb;
            pTarget++;

            *pTarget = (unsigned char)0x80 | (ch & 0x3F);
        }
        pTarget++;


#ifdef FASTLOOP
        // If still have fallback don't do fast loop
        if (fallbackUsed && (ch = EncoderReplacementFallbackBuffer_InternalGetNextChar(&self->buffer.encoderBuffer)) != 0)
            goto ProcessChar;

        int availableChars = pEnd - pSrc;
        int availableBytes = pAllocatedBufferEnd - pTarget;

        // don't fall into the fast decoding loop if we don't have enough characters
        // Note that if we don't have enough bytes, pStop will prevent us from entering the fast loop.
        if (availableChars <= 13) {
            // we are hoping for 1 unsigned char per char
            if (availableBytes < availableChars) {
                // not enough output room.  no pending bits at this point
                ch = 0;
                continue;
            }

            // try to get over the remainder of the ascii characters fast though
            CHAR16_T* pLocalEnd = pEnd; // hint to get pLocalEnd enregistered
            while (pSrc < pLocalEnd) {
                ch = *pSrc;
                pSrc++;

                // Not ASCII, need more than 1 unsigned char per char
                if (ch > 0x7F)
                    goto ProcessChar;

                *pTarget = (unsigned char)ch;
                pTarget++;
            }
            // we are done, let ch be 0 to clear encoder
            ch = 0;
            break;
        }

        // we need at least 1 unsigned char per character, but Convert might allow us to convert
        // only part of the input, so try as much as we can.  Reduce charCount if necessary
        if (availableBytes < availableChars)
        {
            availableChars = availableBytes;
        }

        // FASTLOOP:
        // - optimistic range checks
        // - fallbacks to the slow loop for all special cases, exception throwing, etc.

        // To compute the upper bound, assume that all characters are ASCII characters at this point,
        //  the boundary will be decreased for every non-ASCII character we encounter
        // Also, we need 5 chars reserve for the unrolled ansi decoding loop and for decoding of surrogates
        // If there aren't enough bytes for the output, then pStop will be <= pSrc and will bypass the loop.
        CHAR16_T *pStop = pSrc + availableChars - 5;

        while (pSrc < pStop) {
            ch = *pSrc;
            pSrc++;

            if (ch > 0x7F) {
                goto LongCode;
            }
            *pTarget = (unsigned char)ch;
            pTarget++;

            // get pSrc aligned
            if (((size_t)pSrc & 0x2) != 0) {
                ch = *pSrc;
                pSrc++;
                if (ch > 0x7F) {
                    goto LongCode;
                }
                *pTarget = (unsigned char)ch;
                pTarget++;
            }

            // Run 4 characters at a time!
            while (pSrc < pStop) {
                ch = *(int*)pSrc;
                int chc = *(int*)(pSrc + 2);
                if (((ch | chc) & (int)0xFF80FF80) != 0) {
                    goto LongCodeWithMask;
                }

                // Unfortunately, this is endianness sensitive
#if BIGENDIAN
                if (!treatAsLE)
                {
                    *pTarget = (unsigned char)(ch >> 16);
                    *(pTarget + 1) = (unsigned char)ch;
                    pSrc += 4;
                    *(pTarget + 2) = (unsigned char)(chc >> 16);
                    *(pTarget + 3) = (unsigned char)chc;
                    pTarget += 4;
                }
                else
#else
                {
                    *pTarget = (unsigned char)ch;
                    *(pTarget + 1) = (unsigned char)(ch >> 16);
                    pSrc += 4;
                    *(pTarget + 2) = (unsigned char)chc;
                    *(pTarget + 3) = (unsigned char)(chc >> 16);
                    pTarget += 4;
                }
#endif
            }
            continue;

        LongCodeWithMask:
#if BIGENDIAN
        // be careful about the sign extension
        if (!treatAsLE) ch = (int)(((unsigned int)ch) >> 16);
        else
#else
            ch = (CHAR16_T)ch;
#endif

        pSrc++;

        if (ch > 0x7F) {
            goto LongCode;
        }
        *pTarget = (unsigned char)ch;
        pTarget++;
        continue;

        LongCode:
            // use separate helper variables for slow and fast loop so that the jit optimizations
            // won't get confused about the variable lifetimes
            if (ch <= 0x7FF) {
                // 2 unsigned char encoding
                chd = 0xC0 | (ch >> 6);
            }
            else {
                if (!InRange(ch, HIGH_SURROGATE_START, LOW_SURROGATE_END)) {
                    // 3 unsigned char encoding
                    chd = 0xE0 | (ch >> 12);
                }
                else
                {
                    // 4 unsigned char encoding - high surrogate + low surrogate
                    if (ch > HIGH_SURROGATE_END) {
                        // low without high -> bad, try again in slow loop
                        pSrc -= 1;
                        break;
                    }

                    chd = *pSrc;
                    pSrc++;

                    // if (!IsLowSurrogate(chd)) {
                    if (!InRange(chd, LOW_SURROGATE_START, LOW_SURROGATE_END)) {
                        // high not followed by low -> bad, try again in slow loop
                        pSrc -= 2;
                        break;
                    }

                    ch = chd + (ch << 10) +
                        (0x10000
                        - LOW_SURROGATE_START
                        - (HIGH_SURROGATE_START << 10));

                    *pTarget = (unsigned char)(0xF0 | (ch >> 18));
                    // pStop - this unsigned char is compensated by the second surrogate character
                    // 2 input chars require 4 output bytes.  2 have been anticipated already
                    // and 2 more will be accounted for by the 2 pStop-- calls below.
                    pTarget++;

                    chd = 0x80 | ((ch >> 12) & 0x3F);
                }
                *pTarget = (unsigned char)chd;
                pStop--;                    // 3 unsigned char sequence for 1 char, so need pStop-- and the one below too.
                pTarget++;

                chd = 0x80 | ((ch >> 6) & 0x3F);
            }
            *pTarget = (unsigned char)chd;
            pStop--;                        // 2 unsigned char sequence for 1 char so need pStop--.
            pTarget++;

            *pTarget = (unsigned char)(0x80 | (ch & 0x3F));
            // pStop - this unsigned char is already included
            pTarget++;
        }

        ContractAssertReset(pTarget <= pAllocatedBufferEnd, EncoderReplacementFallbackBuffer_Reset, &self->buffer.encoderBuffer);

#endif // FASTLOOP

        // no pending char at this point
        ch = 0;
    }

    return (int)(pTarget - bytes);
}

static int GetByteCount(UTF8Encoding* self, CHAR16_T *chars, int count)
{
    // For fallback we may need a fallback buffer.
    // We wait to initialize it though in case we don't have any broken input unicode
    bool fallbackUsed = false;
    CHAR16_T *pSrc = chars;
    CHAR16_T *pEnd = pSrc + count;

    // Start by assuming we have as many as count
    int byteCount = count;

    int ch = 0;

    while (true) {
        // SLOWLOOP: does all range checks, handles all special cases, but it is slow
        if (pSrc >= pEnd) {

            if (ch == 0) {
                // Unroll any fallback that happens at the end
                ch = fallbackUsed ? EncoderReplacementFallbackBuffer_InternalGetNextChar(&self->buffer.encoderBuffer) : 0;
                if (ch > 0) {
                    byteCount++;
                    goto ProcessChar;
                }
            }
            else {
                // Case of surrogates in the fallback.
                if (fallbackUsed && self->buffer.encoderBuffer.bFallingBack) {
                    ContractAssertReset(ch >= 0xD800 && ch <= 0xDBFF, EncoderReplacementFallbackBuffer_Reset, &self->buffer.encoderBuffer)

                    ch = EncoderReplacementFallbackBuffer_InternalGetNextChar(&self->buffer.encoderBuffer);
                    byteCount++;

                    if (InRange(ch, LOW_SURROGATE_START, LOW_SURROGATE_END)) {
                        ch = 0xfffd;
                        byteCount++;
                        goto EncodeChar;
                    }
                    else if (ch > 0){
                        goto ProcessChar;
                    }
                    else {
                        byteCount--; // ignore last one.
                        break;
                    }
                }
            }

            if (ch <= 0) {
                break;
            }

            // attempt to encode the partial surrogate (will fallback or ignore it), it'll also subtract 1.
            byteCount++;
            goto EncodeChar;
        }

        if (ch > 0) {
            ContractAssertReset(ch >= 0xD800 && ch <= 0xDBFF, EncoderReplacementFallbackBuffer_Reset, &self->buffer.encoderBuffer);

            // use separate helper variables for local contexts so that the jit optimizations
            // won't get confused about the variable lifetimes
            int cha = *pSrc;

            // count the pending surrogate
            byteCount++;

            // In previous byte, we encountered a high surrogate, so we are expecting a low surrogate here.
            // if (IsLowSurrogate(cha)) {
            if (InRange(cha, LOW_SURROGATE_START, LOW_SURROGATE_END)) {
                // Don't need a real # because we're just counting, anything > 0x7ff ('cept surrogate) will do.
                ch = 0xfffd;
                //                        ch = cha + (ch << 10) +
                //                            (0x10000
                //                            - LOW_SURROGATE_START
                //                            - (HIGH_SURROGATE_START << 10) );

                // Use this next char
                pSrc++;
            }
            // else ch is still high surrogate and encoding will fail (so don't add count)

            // attempt to encode the surrogate or partial surrogate
            goto EncodeChar;
        }

        // If we've used a fallback, then we have to check for it
        if (fallbackUsed)
        {
            ch = EncoderReplacementFallbackBuffer_InternalGetNextChar(&self->buffer.encoderBuffer);
            if (ch > 0)
            {
                // We have an extra byte we weren't expecting.
                byteCount++;
                goto ProcessChar;
            }
        }

        // read next char. The JIT optimization seems to be getting confused when
        // compiling "ch = *pSrc++;", so rather use "ch = *pSrc; pSrc++;" instead
        ch = *pSrc;
        pSrc++;

    ProcessChar:
        if (InRange(ch, HIGH_SURROGATE_START, HIGH_SURROGATE_END)) {
            // we will count this surrogate next time around
            byteCount--;
            continue;
        }
        // either good char or partial surrogate

    EncodeChar:
        // throw exception on partial surrogate if necessary
        if (InRange(ch, HIGH_SURROGATE_START, LOW_SURROGATE_END))
        {
            // Lone surrogates aren't allowed
            // Have to make a fallback buffer if we don't have one
            if (!fallbackUsed)
            {
                // wait on fallbacks if we can
                // For fallback we may need a fallback buffer
                fallbackUsed = true;

                // Set our internal fallback interesting things.
                EncoderReplacementFallbackBuffer_InternalInitialize(&self->buffer.encoderBuffer, chars, chars + count, false);
            }

            // Do our fallback.  Actually we already know its a mixed up surrogate,
            // so the ref pSrc isn't gonna do anything.
            EncoderReplacementFallbackBuffer_InternalFallback(&self->buffer.encoderBuffer, (CHAR16_T)ch, &pSrc);
            RETURN_ON_ERROR(EncoderReplacementFallbackBuffer_Reset, &self->buffer.encoderBuffer)

            // Ignore it if we don't throw (we had preallocated this ch)
            byteCount--;
            ch = 0;
            continue;
        }

        // Count them
        if (ch > 0x7F) {
            if (ch > 0x7FF) {
                // the extra surrogate byte was compensated by the second surrogate character
                // (2 surrogates make 4 bytes.  We've already counted 2 bytes, 1 per char)
                byteCount++;
            }
            byteCount++;
        }

#if WIN64
        // check for overflow
        if (byteCount < 0) {
            break;
        }
#endif

#ifdef FASTLOOP
        // If still have fallback don't do fast loop
        if (fallbackUsed && (ch = EncoderReplacementFallbackBuffer_InternalGetNextChar(&self->buffer.encoderBuffer)) != 0)
        {
            // We're reserving 1 byte for each char by default
            byteCount++;
            goto ProcessChar;
        }

        int availableChars = pEnd - pSrc;

        // don't fall into the fast decoding loop if we don't have enough characters
        if (availableChars <= 13) {
            // try to get over the remainder of the ascii characters fast though
            CHAR16_T* pLocalEnd = pEnd; // hint to get pLocalEnd enregistered
            while (pSrc < pLocalEnd) {
                ch = *pSrc;
                pSrc++;
                if (ch > 0x7F)
                    goto ProcessChar;
            }

            // we are done
            break;
        }

#if WIN64
        // make sure that we won't get a silent overflow inside the fast loop
        // (Fall out to slow loop if we have this many characters)
        availableChars &= 0x0FFFFFFF;
#endif

        // To compute the upper bound, assume that all characters are ASCII characters at this point,
        //  the boundary will be decreased for every non-ASCII character we encounter
        // Also, we need 3 + 4 chars reserve for the unrolled ansi decoding loop and for decoding of surrogates
        CHAR16_T *pStop = pSrc + availableChars - (3 + 4);

        while (pSrc < pStop) {
            ch = *pSrc;
            pSrc++;

            if (ch > 0x7F)                                                  // Not ASCII
            {
                if (ch > 0x7FF)                                             // Not 2 Byte
                {
                    if ((ch & 0xF800) == 0xD800)                            // See if its a Surrogate
                        goto LongCode;
                    byteCount++;
                }
                byteCount++;
            }

            // get pSrc aligned
            if (((size_t)pSrc & 0x2) != 0) {
                ch = *pSrc;
                pSrc++;
                if (ch > 0x7F)                                              // Not ASCII
                {
                    if (ch > 0x7FF)                                         // Not 2 Byte
                    {
                        if ((ch & 0xF800) == 0xD800)                        // See if its a Surrogate
                            goto LongCode;
                        byteCount++;
                    }
                    byteCount++;
                }
            }

            // Run 2 * 4 characters at a time!
            while (pSrc < pStop) {
                ch = *(int*)pSrc;
                int chc = *(int*)(pSrc + 2);
                if (((ch | chc) & (int)0xFF80FF80) != 0)         // See if not ASCII
                {
                    if (((ch | chc) & (int)0xF800F800) != 0)     // See if not 2 Byte
                    {
                        goto LongCodeWithMask;
                    }


                    if ((ch & (int)0xFF800000) != 0)             // Actually 0x07800780 is all we care about (4 bits)
                        byteCount++;
                    if ((ch & (int)0xFF80) != 0)
                        byteCount++;
                    if ((chc & (int)0xFF800000) != 0)
                        byteCount++;
                    if ((chc & (int)0xFF80) != 0)
                        byteCount++;
                }
                pSrc += 4;

                ch = *(int*)pSrc;
                chc = *(int*)(pSrc + 2);
                if (((ch | chc) & (int)0xFF80FF80) != 0)         // See if not ASCII
                {
                    if (((ch | chc) & (int)0xF800F800) != 0)     // See if not 2 Byte
                    {
                        goto LongCodeWithMask;
                    }

                    if ((ch & (int)0xFF800000) != 0)
                        byteCount++;
                    if ((ch & (int)0xFF80) != 0)
                        byteCount++;
                    if ((chc & (int)0xFF800000) != 0)
                        byteCount++;
                    if ((chc & (int)0xFF80) != 0)
                        byteCount++;
                }
                pSrc += 4;
            }
            break;

        LongCodeWithMask:
#if BIGENDIAN
        // be careful about the sign extension
        if (!treatAsLE) ch = (int)(((unsigned int)ch) >> 16);
        else
#else
            ch = (CHAR16_T)ch;
#endif

        pSrc++;

        if (ch <= 0x7F) {
            continue;
        }

        LongCode:
            // use separate helper variables for slow and fast loop so that the jit optimizations
            // won't get confused about the variable lifetimes
            if (ch > 0x7FF) {
                if (InRange(ch, HIGH_SURROGATE_START, LOW_SURROGATE_END)) {
                    // 4 byte encoding - high surrogate + low surrogate

                    int chd = *pSrc;
                    if (
                        ch > HIGH_SURROGATE_END ||
                        !InRange(chd, LOW_SURROGATE_START, LOW_SURROGATE_END))
                    {
                        // Back up and drop out to slow loop to figure out error
                        pSrc--;
                        break;
                    }
                    pSrc++;

                    // byteCount - this byte is compensated by the second surrogate character
                }
                byteCount++;
            }
            byteCount++;

            // byteCount - the last byte is already included
        }
#endif // FASTLOOP

        // no pending char at this point
        ch = 0;
    }

#if WIN64
    // check for overflow
    ContractAssertReset(byteCount >= 0, EncoderReplacementFallbackBuffer_Reset, &self->buffer.encoderBuffer)
#endif
    ContractAssertReset(!fallbackUsed || EncoderReplacementFallbackBuffer_GetRemaining(&self->buffer.encoderBuffer) == 0,
        EncoderReplacementFallbackBuffer_Reset, &self->buffer.encoderBuffer)

    return byteCount;
}

int minipal_utf8_to_utf16_preallocated(
    const char* lpSrcStr,
    int cchSrc,
    CHAR16_T** lpDestStr,
    int cchDest,
    unsigned int dwFlags,
    bool treatAsLE)
{
    int ret;
    errno = 0;

    if (cchSrc < 0)
        cchSrc = strlen(lpSrcStr) + 1;

    UTF8Encoding enc =
    {
        .buffer = { .decoderBuffer = { .fallbackCount = -1, .fallbackIndex = -1, .strDefault = { 0xFFFD, 0 }, .strDefaultLength = 1 } },
        .useFallback = !(dwFlags & MB_ERR_INVALID_CHARS),
#if BIGENDIAN
        .treatAsLE = treatAsLE
#endif
    };

    ret = GetCharCount(&enc, (unsigned char*)lpSrcStr, cchSrc);
    if (cchDest)
    {
        if (ret > cchDest)
        {
            errno = ERROR_INSUFFICIENT_BUFFER;
            ret = 0;
        }

        GetChars(&enc, (unsigned char*)lpSrcStr, cchSrc, (CHAR16_T*)*lpDestStr, ret);
        if (errno) ret = 0;
    }

    return ret;
}

static int utf16_to_utf8_preallocated(
    const CHAR16_T* lpSrcStr,
    int cchSrc,
    char** lpDestStr,
    int cchDest,
    bool treatAsLE)
{
    int ret;
    errno = 0;

    if (cchSrc < 0)
        cchSrc = minipal_wcslen(lpSrcStr) + 1;

    UTF8Encoding enc =
    {
        // repeat replacement char (0xFFFD) twice for a surrogate pair
        .buffer = { .encoderBuffer = { .fallbackCount = -1, .fallbackIndex = -1, .strDefault = { 0xFFFD, 0xFFFD, 0 }, .strDefaultLength = 2 } },
        .useFallback = true,
#if BIGENDIAN
        .treatAsLE = treatAsLE
#endif
    };

    ret = GetByteCount(&enc, (CHAR16_T*)lpSrcStr, cchSrc);
    if (cchDest)
    {
        if (ret > cchDest)
        {
            errno = ERROR_INSUFFICIENT_BUFFER;
            ret = 0;
        }

        GetBytes(&enc, (CHAR16_T*)lpSrcStr, cchSrc, (unsigned char*)*lpDestStr, ret);
        if (errno) ret = 0;
    }

    return ret;
}

int minipal_utf16_to_utf8_preallocated(
    const CHAR16_T* lpSrcStr,
    int cchSrc,
    char** lpDestStr,
    int cchDest)
{
    return utf16_to_utf8_preallocated(lpSrcStr, cchSrc, lpDestStr, cchDest, false);
}

int minipal_utf8_to_utf16_allocate(
    const char* lpSrcStr,
    int cchSrc,
    CHAR16_T** lpDestStr,
    unsigned int dwFlags,
    bool treatAsLE)
{
    int cchDest = minipal_utf8_to_utf16_preallocated(lpSrcStr, cchSrc, NULL, 0, dwFlags, treatAsLE);
    if (cchDest > 0)
    {
        *lpDestStr = (CHAR16_T*)malloc((cchDest + 1) * sizeof(CHAR16_T));
        cchDest = minipal_utf8_to_utf16_preallocated(lpSrcStr, cchSrc, lpDestStr, cchDest, dwFlags, treatAsLE);
        (*lpDestStr)[cchDest] = '\0';
    }
    return cchDest;
}

int minipal_utf16_to_utf8_allocate(
    const CHAR16_T* lpSrcStr,
    int cchSrc,
    char** lpDestStr,
    bool treatAsLE)
{
    int cchDest = utf16_to_utf8_preallocated(lpSrcStr, cchSrc, NULL, 0, treatAsLE);
    if (cchDest > 0)
    {
        *lpDestStr = (char*)malloc((cchDest + 1) * sizeof(char));
        cchDest = utf16_to_utf8_preallocated(lpSrcStr, cchSrc, lpDestStr, cchDest, treatAsLE);
        (*lpDestStr)[cchDest] = '\0';
    }
    return cchDest;
}
