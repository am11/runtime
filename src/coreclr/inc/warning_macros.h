#ifndef __WARNING_MACROS_H__
#define __WARNING_MACROS_H__

#ifndef FALLTHROUGH
#if __has_cpp_attribute(fallthrough)
#define FALLTHROUGH [[fallthrough]]
#else
#define FALLTHROUGH
#endif // __has_cpp_attribute(fallthrough)
#endif //!FALLTHROUGH

#endif //__WARNING_MACROS_H__
